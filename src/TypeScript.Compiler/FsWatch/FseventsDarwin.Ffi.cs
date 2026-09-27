// ported from: internal/fswatch/fsevents_darwin_ffi.go:1-598（+ fsevents_darwin_ffi.s / _amd64.s / _arm64.s 的等价改写）
//
// C# 映射与偏离（逐条）：
// 1) **`.s` 汇编跳板整体不移植**。Go 的三段式（`//go:cgo_import_dynamic` 从框架导入符号 → `.s` 的 TEXT 跳板 →
//    GLOBL/DATA 导出跳板地址给 `syscall.syscall6`）在 C# 里塌缩成一条 `DllImport`：CLR 直接按平台 ABI 调用
//    CoreFoundation.framework / CoreServices.framework / libSystem.B.dylib 的同名符号。逐个对应如下
//    （左 = Go 的导入与包装，右 = 本文件的 DllImport）：
//      cfRelease            → CoreFoundation.Release（Canonicalize.cs，`CFRelease`）
//      cfStringCreate       → CoreFoundation.StringCreate（Canonicalize.cs，`CFStringCreateWithCString`）
//      cfArrayCreate        → FseventsFfi.CFArrayCreate
//      cfArrayGetValueAtIndex → FseventsFfi.CFArrayGetValueAtIndex
//      cfStringToNFC 等 NFC 归一化 → CoreFoundation.StringToNFC（Canonicalize.cs，同一批 CF 符号）
//      dispatchQueueCreate  → FseventsFfi.DispatchQueueCreate
//      dispatchRelease      → FseventsFfi.DispatchRelease
//      dispatchSync        → FseventsFfi.DispatchSyncF（work = `fse_dispatch_noop_addr` → DispatchNoopPointer）
//      fsEventStreamCreate  → FseventsFfi.FSEventStreamCreate（**不再需要 arch 跳板**：跳板只做两件事——把
//                             latency 的 float64 从整数寄存器搬到 xmm0、把 flags 硬编码成 0x11。C# 里
//                             `double latency` 由 CLR 放进浮点寄存器，flags 作为第 7 个显式实参传入
//                             `FseventsFfi.CreateFlags`（= 0x11，与跳板逐字节一致）。）
//      fsEventStreamSetDispatchQueue/Start/Stop/Invalidate/Release/FlushSync → 同名 DllImport
//      fsEventsGetCurrentEventID → FseventsFfi.FSEventsGetCurrentEventId（`FSEventsGetCurrentEventId`）
//      libcFree             → Marshal.FreeHGlobal（**分配侧也换成 Marshal.AllocHGlobal**，分配/释放严格配对；
//                             不再 DllImport `malloc`/`free`，见第 4 条）
//      fse_write / fse___error / fse_memcpy（只被 asm 直接调用） → FseventsFfi.Write / FseventsFfi.Errno /
//                              FseventsFfi.Memcpy
//      fse_CFRetain（只被 asm 直接调用） → FseventsFfi.CFRetain
//    任务书里另列的 `CFArrayGetCount` / `dispatch_queue_set_specific` / `FSEventStreamGetLatestEventId`
//    **Go 从未使用**（`fsevents_darwin_ffi.go` 全文只在 `CFArrayGetValueAtIndex`、`FSEventsGetCurrentEventId` 上
//    出现），按"以 Go 实际用到的为准"不声明。
// 2) 回调 `fsEventsCallbackASM` → `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` 的
//    `FsEventsCallbackNative`。Go 用汇编写它是因为汇编程"永不进 Go ABI"，可以只做 retain/copy/write 就返回；
//    C# 里 `[UnmanagedCallersOnly]` 就是那条"原生可调、不经过委托/封送"的通道，因此：**没有跳板、没有 pipe 之外
//    的额外机制、也没有把托管对象地址交给原生代码**。
//    取函数地址：`(IntPtr)(delegate* unmanaged[Cdecl]<…>)&FsEventsCallbackNative`。函数指针类型是 C# 语言级
//    特性，**不需要 `unsafe` 上下文**（本项目未开 AllowUnsafeBlocks，已用 net11.0 探针工程实测编译通过）；
//    也没有用反射（`MethodHandle.GetFunctionPointer`）或 `Marshal.GetFunctionPointerForDelegate`——前者在
//    AOT/裁剪下不可靠（本仓 IsAotCompatible=true），后者要额外保活一个托管委托。
// 3) `FSEventStreamContext.info` 与 GCHandle（替代 `runtime.Pinner`）：Go 把 `cb` 的裸地址放进 `ctx.info`，
//    asm 按 `offset 0` 读 `eventPipeWrite`。C# 交出去的是 `GCHandle.ToIntPtr(GCHandle.Alloc(cb))`，回调里
//    `GCHandle.FromIntPtr(info)` 取回 `StreamCallback`，再读它的 `_eventPipeWriteFd` 字段。原因：托管引用类型
//    的地址在 offset 0 是方法表指针，**不能**用 offset 寻址读 fd；GCHandle 同时承担 Go 的 `runtime.Pinner` 的
//    保活职责（`GCHandle.Alloc`/`Free` 与 `Pin`/`Unpin` 一一对应，由 FseventsStream 持有）。
//    因此 `streamCallback` 那条"offset 0 必须是 eventPipeWrite"的汇编布局约束在 C# 侧不存在（字段由 CLR 布局）。
// 4) 负载（`fsEventsCallbackPayload`）的生命周期：asm 用 `malloc` + `memcpy` 在 C 堆上建 payload，把**payload
//    指针本身**写进管道（8 字节），eventLoop 读回指针后走 `fsEventsCallback`，由 `defer payload.close()` 释放
//    （`CFRelease(paths)` + `free(flags)` + `free(ids)` + `free(payload)`）。C# 完全保持这条流水线：
//    分配用 `Marshal.AllocHGlobal`（写端 = GCD 线程），拷贝用 libc `memcpy`（C# 没有 `IntPtr`→`IntPtr` 的
//    `Marshal.Copy` 重载，已实测确认），释放用 `Marshal.FreeHGlobal`（读端 = eventLoop 线程），
//    **跨线程所有权转移点就是 write 成功那一刻**（与 Go 完全相同）：
//      write 成功 → payload 归 eventLoop（`FsEventsCallbackPayload.Close` 释放）
//      write 失败/未写出 8 字节 → 回调自己把 payload/flags/ids/paths 全部释放（= asm 的 freePayload → freeIDs
//      → freeFlags → releasePaths 级联）
// 5) 管道同步：`pipe(2)` 两条 fd，两端 `CloseOnExec`（fcntl F_SETFD/FD_CLOEXEC，macOS 上是 2/1）；读端包成
//    `FileStream`（`SafeFileHandle` 拥有所有权），`ReadExactly` = Go 的 `io.ReadFull`；写端是裸 fd，
//    用 libc `write` + `__error` 做 EINTR 重试（= asm 的 writeAgain 循环）。每流一条串行 dispatch queue
//    （`dispatch_queue_create`，label 与 Go 相同），使各流的回调不会互相队头阻塞。
// 6) `chan struct{} done`（close = 就绪）→ `TaskCompletionSource`（`TrySetResult` = `close`，`Task.Wait` =
//    `<-cb.done`）；Go 的 `go cb.eventLoop()` → `Task.Run(cb.EventLoop, TaskCreationOptions.LongRunning)`
//    （管道上的阻塞读需要专属线程，见 FseventsDarwin.cs 文件头第 5 条）。
// 7) Go 的 `type streamCallback struct { eventPipeWrite uintptr; eventFile *os.File; queue uintptr; done chan struct{};
//    watches []fseventsWatchSnapshot }`：字段名照抄（`_camelCase`），并为 C# 的读缓冲/payload 槽补两个
//    `Marshal.AllocHGlobal(8)`（Go 的 asm 把 payload 槽开在自己的栈帧上，C# 不能借用托管栈，故显式分配，
//    在 `Close` 里、eventLoop 线程汇合之后释放）。`Close()` 另加一个 `_closed` 守卫使其幂等（防御性偏离：
//    Go 的 `close` 依赖调用点只调用一次，重复调用会 double close 那个 fd）。
// 8) 回调里**不能有异常逃逸**：`[UnmanagedCallersOnly]` 方法抛异常会终止进程。Go 的 asm 同样没有错误通道
//    （malloc 失败就丢弃这一批）。因此 C# 侧对回调体整体 try/catch 后静默丢弃这一批事件——**也不调用
//    `Debug.Fail`**（它按移植约定会抛 `DebugFailureException`，在这个上下文里等于崩溃）。
// 8b) 对称地，eventLoop 线程上 `fsEventsCallback` 若抛异常（例如 `foldNativePath` 的 Debug.Fail 路径），
//    C# 的行为是"该流的 eventLoop 异常结束"（`finally` 仍会放行 `_done`，所以 teardown 不会挂死），而 Go 的
//    goroutine panic 会直接终止进程。这是本移植唯一"更安静"的失败模式，刻意保留（C# 里能做的只有不让它
//    破坏 teardown 契约）。
// 9) 本机是 win32，本文件**无法运行验证**，只能静态对照 Apple 头文件/Go 汇编：
//    (a) FFI 形参顺序与位宽：`FSEventStreamCreate` 7 参（allocator, callback, context*, paths CFArrayRef,
//        since uint64, latency double, flags uint32）、`FSEventStreamStart` 返回 `Boolean` = uint8（C# 用 `byte`
//        与 Go 的 `uint8` 逐字节一致，不用 `bool`，避免 arm64 上把 1 字节返回值当 4 字节读）；
//    (b) `FSEventStreamContext` = 40 字节（`CFIndex version` 8 + 4 指针），amd64/arm64 相同；
//    (c) 回调签名 `(ConstFSEventStreamRef, void *info, size_t numEvents, void *eventPaths,
//        const FSEventStreamEventFlags[], const FSEventStreamEventId[])`；`UseCFTypes` 下 eventPaths 是 CFArrayRef、
//        flags 是 uint32[]、ids 是 uint64[]；
//    (d) libSystem 符号名 `pipe`/`fcntl`/`close`/`write`/`memcpy`/`__error`/`dispatch_queue_create`/
//        `dispatch_release`/`dispatch_sync_f` 与 errno（EINTR = 4）；
//    (e) `Marshal.AllocHGlobal` 的分配器与 libc 的 `memcpy` 搭配（同一进程堆，跨线程使用安全）、
//        `GCHandle.FromIntPtr` 在 libdispatch OS 线程上的可用性。
namespace TypeScript.FsWatch;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Go: <c>fsEventStreamContext</c>（C 的 <c>FSEventStreamContext</c>）。
/// <para>布局（64 位平台，amd64 与 arm64 相同，共 40 字节）：
/// version = +0（CFIndex，8 字节）、info = +8、retain = +16、release = +24、copyDescription = +32。</para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FseventStreamContext
{
    /// <summary>Go: <c>version int</c>（CFIndex）。</summary>
    internal nint Version;

    /// <summary>Go: <c>info uintptr</c>（本移植传 GCHandle，见 Ffi 文件头第 3 条）。</summary>
    internal IntPtr Info;

    /// <summary>Go: <c>retain uintptr</c>（恒为 NULL：不使用 context 的 retain/release 回调）。</summary>
    internal IntPtr Retain;

    /// <summary>Go: <c>release uintptr</c>（恒为 NULL）。</summary>
    internal IntPtr Release;

    /// <summary>Go: <c>copyDescription uintptr</c>（恒为 NULL）。</summary>
    internal IntPtr CopyDescription;
}

/// <summary>
/// Go: <c>type fsEventsCallbackPayload struct</c>。在**非托管内存**上的 32 字节记录，由 GCD 线程上的回调填好、
/// 经管道把指针交给 eventLoop 线程，读端用 <see cref="Read"/> 取值、用 <see cref="Close"/> 释放。
/// </summary>
internal readonly struct FsEventsCallbackPayload
{
    /// <summary>Go: asm 里 `malloc($32)` 的字节数（4 个 uintptr；darwin 只有 64 位目标）。</summary>
    internal static readonly int Size = IntPtr.Size * 4;

    /// <summary>该记录在非托管内存里的地址（= asm 写进管道的那 8 字节；Close 时先释放它）。</summary>
    internal readonly IntPtr _self;

    /// <summary>Go: <c>numEvents uintptr</c>。</summary>
    internal readonly nuint _numEvents;

    /// <summary>Go: <c>paths uintptr</c>（被 CFRetain 过的 CFArrayRef，由本记录持有）。</summary>
    internal readonly IntPtr _paths;

    /// <summary>Go: <c>flags uintptr</c>（numEvents 个 uint32 的副本）。</summary>
    internal readonly IntPtr _flags;

    /// <summary>Go: <c>ids uintptr</c>（numEvents 个 uint64 的副本）。</summary>
    internal readonly IntPtr _ids;

    private FsEventsCallbackPayload(IntPtr self, nuint numEvents, IntPtr paths, IntPtr flags, IntPtr ids)
    {
        _self = self;
        _numEvents = numEvents;
        _paths = paths;
        _flags = flags;
        _ids = ids;
    }

    /// <summary>Go 里 eventLoop 直接读 <c>*payload</c> 的字段（C# 从非托管地址逐字段读回）。</summary>
    internal static FsEventsCallbackPayload Read(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return default; // Go 的 nil payload：Close 是 no-op，调用点也整批跳过。
        }

        return new FsEventsCallbackPayload(
            pointer,
            (nuint)(ulong)Marshal.ReadInt64(pointer),
            Marshal.ReadIntPtr(pointer, IntPtr.Size),
            Marshal.ReadIntPtr(pointer, IntPtr.Size * 2),
            Marshal.ReadIntPtr(pointer, IntPtr.Size * 3));
    }

    /// <summary>Go: <c>func (p *fsEventsCallbackPayload) close()</c>。</summary>
    internal void Close()
    {
        if (_self == IntPtr.Zero)
        {
            return;
        }

        if (_paths != IntPtr.Zero)
        {
            CoreFoundation.Release(_paths);
        }

        if (_flags != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_flags);
        }

        if (_ids != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ids);
        }

        Marshal.FreeHGlobal(_self);
    }
}

/// <summary>
/// Go: <c>type streamCallback struct</c>。每流一份：管道写端 fd、读端 FileStream、串行 dispatch queue、
/// eventLoop 就绪信号、以及该流的 watch 快照。
/// </summary>
internal sealed class StreamCallback
{
    /// <summary>Go: asm 的 EINTR 常量（macOS <c>&lt;errno.h&gt;</c>：EINTR = 4）。</summary>
    private const int _eintr = 4;

    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] _readBytes = new byte[IntPtr.Size];
    private FileStream? _eventFile;
    private IntPtr _readBuffer;
    private IntPtr _payloadSlot;
    private bool _closed;

    /// <summary>Go: <c>eventPipeWrite uintptr</c>（-1 = 已关闭；回调从这里取写 fd）。</summary>
    internal int _eventPipeWriteFd = -1;

    /// <summary>Go: <c>queue uintptr</c>（本流的串行 dispatch queue）。</summary>
    internal IntPtr _queue;

    /// <summary>Go: <c>watches []fseventsWatchSnapshot</c>（建流时的快照，事件路由只认这一份）。</summary>
    internal FseventsWatchSnapshot[] _watches = [];

    /// <summary>
    /// Go: <c>func newStreamCallback(watches []fseventsWatchSnapshot) (*streamCallback, error)</c>。
    /// 建管道（两端 CloseOnExec）+ 本流串行队列，然后起 eventLoop 线程。
    /// </summary>
    internal static StreamCallback New(FseventsWatchSnapshot[] watches)
    {
        var fds = Marshal.AllocHGlobal(2 * sizeof(int));
        int readFd;
        int writeFd;
        try
        {
            if (FseventsFfi.Pipe(fds) != 0)
            {
                var errno = FseventsFfi.Errno();
                throw new IOException($"pipe: {FseventsFfi.ErrnoText(errno)}");
            }

            readFd = Marshal.ReadInt32(fds);
            writeFd = Marshal.ReadInt32(fds, sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(fds);
        }

        FseventsFfi.SetCloseOnExec(readFd); // Go: unix.CloseOnExec(eventPipe[0])
        FseventsFfi.SetCloseOnExec(writeFd); // Go: unix.CloseOnExec(eventPipe[1])

        var queue = FseventsFfi.DispatchQueueCreate("typescript.fswatch.fsevents.stream", IntPtr.Zero);
        if (queue == IntPtr.Zero)
        {
            _ = FseventsFfi.CloseFd(readFd);
            _ = FseventsFfi.CloseFd(writeFd);
            throw FseventsErrors.StreamCreateNull(); // Go: return nil, errStreamCreateNull
        }

        var cb = new StreamCallback
        {
            _eventPipeWriteFd = writeFd,
            _queue = queue,
            _watches = (FseventsWatchSnapshot[])watches.Clone(), // Go: slices.Clone(watches)
        };

        cb._eventFile = new FileStream(
            new SafeFileHandle(readFd, ownsHandle: true),
            FileAccess.Read,
            bufferSize: IntPtr.Size,
            isAsync: false);
        cb._readBuffer = Marshal.AllocHGlobal(IntPtr.Size);
        cb._payloadSlot = Marshal.AllocHGlobal(IntPtr.Size);

        // Go: go cb.eventLoop()（LongRunning = 专属后台线程，因为 eventLoop 阻塞在管道读上）。
        _ = Task.Factory.StartNew(cb.EventLoop, TaskCreationOptions.LongRunning);
        return cb;
    }

    /// <summary>Go: <c>func (cb *streamCallback) waitDispatchQueue()</c>。同步本队列 = 等已排队的回调跑完。</summary>
    internal void WaitDispatchQueue()
    {
        if (_queue != IntPtr.Zero)
        {
            // Go: dispatchSync(cb.queue, 0, fse_dispatch_noop_addr)
            FseventsFfi.DispatchSyncF(_queue, IntPtr.Zero, FseventsFfi.DispatchNoopPointer);
        }
    }

    /// <summary>
    /// Go: <c>func (cb *streamCallback) close()</c>。关上写端 → eventLoop 读到 EOF 退出 → 汇合 →
    /// 关读端 FileStream → 释放读缓冲/负载槽与队列。（`_closed` 只是防御性幂等；Go 依赖调用点只调一次。）
    /// </summary>
    internal void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        _ = FseventsFfi.CloseFd(_eventPipeWriteFd); // Go: unix.Close(int(cb.eventPipeWrite))
        _eventPipeWriteFd = -1;
        _done.Task.Wait(); // Go: <-cb.done
        _eventFile?.Dispose(); // Go: cb.eventFile.Close()
        _eventFile = null;

        if (_readBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_readBuffer);
            _readBuffer = IntPtr.Zero;
        }

        if (_payloadSlot != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadSlot);
            _payloadSlot = IntPtr.Zero;
        }

        if (_queue != IntPtr.Zero)
        {
            FseventsFfi.DispatchRelease(_queue); // Go: dispatchRelease(cb.queue)
            _queue = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Go: <c>func (cb *streamCallback) eventLoop()</c>（本移植起在专属后台线程上，见 Ffi 文件头第 6 条）。
    /// 从管道读 8 字节的 payload 指针，再做分类（业务逻辑只在这里，绝不在 GCD 线程上）。
    /// </summary>
    private void EventLoop()
    {
        try
        {
            while (true)
            {
                // Go: io.ReadFull(cb.eventFile, buf)，buf 是 8 字节的 payload 指针槽。
                try
                {
                    _eventFile!.ReadExactly(_readBytes);
                }
                catch (Exception)
                {
                    return; // 管道关闭或读错误 → 收工（Go: return）
                }

                Marshal.Copy(_readBytes, 0, _readBuffer, IntPtr.Size);
                FSEventsBackend.FsEventsCallback(this, Marshal.ReadIntPtr(_readBuffer));
            }
        }
        finally
        {
            _done.TrySetResult(); // Go: defer close(cb.done)
        }
    }

    /// <summary>
    /// Go: asm 的 `write(info-&gt;eventPipeWrite, &amp;payload, 8)` 循环。true = 8 字节已写出，payload 所有权已转移给
    /// eventLoop；false = 写失败，调用方负责释放 payload（= asm 的 freePayload 级联）。
    /// </summary>
    internal bool WritePayload(IntPtr payload)
    {
        Marshal.WriteIntPtr(_payloadSlot, payload);
        while (true)
        {
            var written = FseventsFfi.Write(_eventPipeWriteFd, _payloadSlot, (nuint)IntPtr.Size);
            if (written == IntPtr.Size)
            {
                return true;
            }

            if (written == -1 && FseventsFfi.Errno() == _eintr)
            {
                continue; // asm: CMPL AX,$4 (EINTR); JEQ writeAgain
            }

            return false;
        }
    }
}

/// <summary>
/// Go: <c>fsevents_darwin_ffi.go</c> 的 cgo-free FFI 层 + `fsevents_darwin_ffi*.s` 的回调/跳板。
/// 所有成员都只在 macOS 上被调用。
/// </summary>
internal static class FseventsFfi
{
    /// <summary>Go: <c>kFSEventStreamCreateFlagUseCFTypes</c>（0x1）。</summary>
    internal const uint CreateFlagUseCFTypes = 0x0000_0001;

    /// <summary>Go: <c>kFSEventStreamCreateFlagFileEvents</c>（0x10）。</summary>
    internal const uint CreateFlagFileEvents = 0x0000_0010;

    /// <summary>
    /// Go: 汇编跳板里硬编码的 flags = <c>UseCFTypes | FileEvents</c>（0x11）。见 Ffi 文件头第 1 条：
    /// 跳板不再需要，但取值必须与之一致。
    /// </summary>
    internal const uint CreateFlags = CreateFlagUseCFTypes | CreateFlagFileEvents;

    /// <summary>macOS <c>&lt;fcntl.h&gt;</c> 的 <c>F_SETFD</c>。</summary>
    private const int _fSetFd = 2;

    /// <summary>macOS <c>&lt;fcntl.h&gt;</c> 的 <c>FD_CLOEXEC</c>。</summary>
    private const int _fdCloexec = 1;

    private const string _coreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string _coreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string _libSystem = "/usr/lib/libSystem.B.dylib";

    /// <summary>Go: <c>·fse_dispatch_noop_addr</c>（fsevents_darwin_ffi.s 的裸 `RET`）。</summary>
    internal static readonly IntPtr DispatchNoopPointer =
        (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&DispatchNoop;

    /// <summary>Go: <c>var fsEventsCallbackAsmAddr</c>（`fsevents_darwin_ffi_{amd64,arm64}.s` 里的回调符号）。</summary>
    internal static readonly IntPtr FsEventsCallbackPointer =
        (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, nuint, IntPtr, IntPtr, IntPtr, void>)&FsEventsCallbackNative;

    /// <summary>Go: <c>fse_dispatch_noop</c>。dispatch_sync_f 只需要一个可调用的空函数。</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DispatchNoop(IntPtr context)
    {
    }

    /// <summary>
    /// Go: <c>fsEventsCallbackASM</c>。FSEvents 在 libdispatch 的 OS 线程上按 C 调用约定调进来；本方法**只**
    /// 做「CFRetain(CFArray) → 拷 flags/ids → 把 payload 指针写进管道 → 返回」，业务分类在 eventLoop 线程上做。
    /// 参数顺序与 asm 完全一致：DI=streamRef, SI=info, DX=numEvents, CX=paths, R8=flags, R9=ids。
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FsEventsCallbackNative(IntPtr streamRef, IntPtr info, nuint numEvents, IntPtr paths, IntPtr flags, IntPtr ids)
    {
        var retainedPaths = IntPtr.Zero;
        var flagsCopy = IntPtr.Zero;
        var idsCopy = IntPtr.Zero;
        var payload = IntPtr.Zero;
        var handedOff = false;
        try
        {
            // Go: asm 的 info 实参是 streamCallback 的裸地址；本移植是 StreamCallback 的 GCHandle（见文件头第 3 条）。
            // 整段（含这次解析）都在 try 里：异常绝不能逃出 [UnmanagedCallersOnly] 方法。
            if (GCHandle.FromIntPtr(info).Target is not StreamCallback cb)
            {
                return;
            }

            // asm: CALL fse_CFRetain; TESTQ AX,AX; JEQ done —— FSEvents 拥有回调实参，retain 后才能跨线程用。
            retainedPaths = CFRetain(paths);
            if (retainedPaths == IntPtr.Zero)
            {
                return;
            }

            // asm: malloc(numEvents*4) + memcpy(flags)。
            flagsCopy = Marshal.AllocHGlobal((nint)(numEvents * 4));
            _ = Memcpy(flagsCopy, flags, numEvents * 4);

            // asm: malloc(numEvents*8) + memcpy(ids)。
            idsCopy = Marshal.AllocHGlobal((nint)(numEvents * 8));
            _ = Memcpy(idsCopy, ids, numEvents * 8);

            // asm: malloc(32) 后按 offset 0/8/16/24 写 numEvents/paths/flags/ids。
            payload = Marshal.AllocHGlobal(FsEventsCallbackPayload.Size);
            Marshal.WriteIntPtr(payload, 0, (IntPtr)(nint)numEvents);
            Marshal.WriteIntPtr(payload, IntPtr.Size, retainedPaths);
            Marshal.WriteIntPtr(payload, IntPtr.Size * 2, flagsCopy);
            Marshal.WriteIntPtr(payload, IntPtr.Size * 3, idsCopy);

            handedOff = cb.WritePayload(payload);
        }
        catch (Exception)
        {
            // 见文件头第 8 条：异常绝不能逃出 [UnmanagedCallersOnly]（会终止进程），Go 的 asm 也没有错误通道；
            // 与 asm 的 malloc 失败分支一致——丢掉这一批，eventLoop 继续跑。
        }
        finally
        {
            if (!handedOff)
            {
                if (payload != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(payload);
                }

                if (idsCopy != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(idsCopy);
                }

                if (flagsCopy != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(flagsCopy);
                }

                if (retainedPaths != IntPtr.Zero)
                {
                    CoreFoundation.Release(retainedPaths);
                }
            }
        }
    }

    /// <summary>Go: <c>func cfRelease(ref uintptr)</c> —— 这里用 Canonicalize.cs 的同名封装（同一 CFRelease）。</summary>
    internal static void Release(IntPtr reference) => CoreFoundation.Release(reference);

    /// <summary>Go: <c>unix.CloseOnExec(fd)</c> = <c>fcntl(fd, F_SETFD, FD_CLOEXEC)</c>。</summary>
    internal static void SetCloseOnExec(int fd) => _ = Fcntl(fd, _fSetFd, _fdCloexec);

    /// <summary>Go: asm 的 <c>fse___error</c>（取 errno 地址后读 int）。</summary>
    internal static int Errno() => Marshal.ReadInt32(ErrnoLocation());

    /// <summary>Go 的 errno 文本（用于 pipe 失败的错误消息；与 InotifyLinux.cs 的同类写法一致）。</summary>
    internal static string ErrnoText(int errno) => new Win32Exception(errno).Message;

    // -----------------------------------------------------------------------
    // CoreFoundation（NFC 归一化那一批 CF 符号在 Canonicalize.cs 的 CoreFoundation 里，见文件头第 1 条）
    // -----------------------------------------------------------------------

    /// <summary>Go: <c>cfArrayCreate(0, &amp;values[0], count, 0)</c>。</summary>
    [DllImport(_coreFoundation, EntryPoint = "CFArrayCreate")]
    internal static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr values, nint count, IntPtr callbacks);

    /// <summary>Go: <c>cfArrayGetValueAtIndex(array, index)</c>。</summary>
    [DllImport(_coreFoundation, EntryPoint = "CFArrayGetValueAtIndex")]
    internal static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    /// <summary>Go: <c>fse_CFRetain</c>（只有回调汇编直接调用它）。</summary>
    [DllImport(_coreFoundation, EntryPoint = "CFRetain")]
    internal static extern IntPtr CFRetain(IntPtr reference);

    // -----------------------------------------------------------------------
    // libdispatch
    // -----------------------------------------------------------------------

    /// <summary>Go: <c>dispatchQueueCreate(label)</c>（第二参 NULL = 串行队列）。</summary>
    [DllImport(_libSystem, EntryPoint = "dispatch_queue_create")]
    internal static extern IntPtr DispatchQueueCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string label, IntPtr attr);

    /// <summary>Go: <c>dispatchRelease(obj)</c>。</summary>
    [DllImport(_libSystem, EntryPoint = "dispatch_release")]
    internal static extern void DispatchRelease(IntPtr obj);

    /// <summary>Go: <c>dispatchSync(queue, context, work)</c>（work = <see cref="DispatchNoopPointer"/>）。</summary>
    [DllImport(_libSystem, EntryPoint = "dispatch_sync_f")]
    internal static extern void DispatchSyncF(IntPtr queue, IntPtr context, IntPtr work);

    // -----------------------------------------------------------------------
    // CoreServices / FSEvents
    // -----------------------------------------------------------------------

    /// <summary>
    /// Go: <c>fsEventStreamCreate(allocator, callback, ctx, paths, since, latency)</c>。
    /// 与 Go 的差异只有两条（都在文件头第 1 条）：latency 由 CLR 放进浮点寄存器（不需要跳板搬寄存器），
    /// flags 作为第 7 个显式形参传入。
    /// </summary>
    [DllImport(_coreServices, EntryPoint = "FSEventStreamCreate")]
    internal static extern IntPtr FSEventStreamCreate(
        IntPtr allocator,
        IntPtr callback,
        in FseventStreamContext context,
        IntPtr pathsToWatch,
        ulong sinceWhen,
        double latency,
        uint flags);

    /// <summary>Go: <c>fsEventStreamSetDispatchQueue(stream, queue)</c>。</summary>
    [DllImport(_coreServices, EntryPoint = "FSEventStreamSetDispatchQueue")]
    internal static extern void FSEventStreamSetDispatchQueue(IntPtr stream, IntPtr queue);

    /// <summary>
    /// Go: <c>fsEventStreamStart(stream) uint8</c>。C 的返回类型是 <c>Boolean</c> = uint8（**不是** 4 字节
    /// <c>BOOL</c>），所以这里也用 <c>byte</c>，与 Go 的 `uint8(r1)` 逐字节一致。
    /// </summary>
    [DllImport(_coreServices, EntryPoint = "FSEventStreamStart")]
    internal static extern byte FSEventStreamStart(IntPtr stream);

    /// <summary>Go: <c>fsEventStreamFlushSync(stream)</c>。</summary>
    [DllImport(_coreServices, EntryPoint = "FSEventStreamFlushSync")]
    internal static extern void FSEventStreamFlushSync(IntPtr stream);

    /// <summary>Go: <c>fsEventsGetCurrentEventID()</c>（FSEventsGetCurrentEventId），用作 watcher.sequence。</summary>
    [DllImport(_coreServices, EntryPoint = "FSEventsGetCurrentEventId")]
    internal static extern ulong FsEventsGetCurrentEventId();

    /// <summary>Go: <c>fsEventStreamStop(stream)</c>。</summary>
    [DllImport(_coreServices, EntryPoint = "FSEventStreamStop")]
    internal static extern void FSEventStreamStop(IntPtr stream);

    /// <summary>Go: <c>fsEventStreamInvalidate(stream)</c>。</summary>
    [DllImport(_coreServices, EntryPoint = "FSEventStreamInvalidate")]
    internal static extern void FSEventStreamInvalidate(IntPtr stream);

    /// <summary>Go: <c>fsEventStreamRelease(stream)</c>。</summary>
    [DllImport(_coreServices, EntryPoint = "FSEventStreamRelease")]
    internal static extern void FSEventStreamRelease(IntPtr stream);

    // -----------------------------------------------------------------------
    // libSystem：管道与回调汇编直接用到的 libc 符号
    // -----------------------------------------------------------------------

    /// <summary>Go: <c>unix.Pipe(eventPipe[:])</c>。</summary>
    [DllImport(_libSystem, EntryPoint = "pipe", SetLastError = true)]
    internal static extern int Pipe(IntPtr fds);

    /// <summary>Go: <c>unix.CloseOnExec</c> / <c>unix.Close</c> 底下的 <c>fcntl</c>。</summary>
    [DllImport(_libSystem, EntryPoint = "fcntl", SetLastError = true)]
    internal static extern int Fcntl(int fd, int cmd, int arg);

    /// <summary>Go: <c>unix.Close(fd)</c>。</summary>
    [DllImport(_libSystem, EntryPoint = "close", SetLastError = true)]
    internal static extern int CloseFd(int fd);

    /// <summary>Go: asm 的 <c>fse_write</c>。</summary>
    [DllImport(_libSystem, EntryPoint = "write", SetLastError = true)]
    internal static extern nint Write(int fd, IntPtr buffer, nuint count);

    /// <summary>Go: asm 的 <c>fse_memcpy</c>（C# 没有 IntPtr→IntPtr 的 <c>Marshal.Copy</c> 重载）。</summary>
    [DllImport(_libSystem, EntryPoint = "memcpy")]
    internal static extern IntPtr Memcpy(IntPtr destination, IntPtr source, nuint count);

    /// <summary>Go: asm 的 <c>fse___error</c>。</summary>
    [DllImport(_libSystem, EntryPoint = "__error")]
    private static extern IntPtr ErrnoLocation();
}
