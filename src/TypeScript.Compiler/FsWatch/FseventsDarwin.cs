// ported from: internal/fswatch/fsevents_darwin.go:1-665
//
// C# 映射与偏离（逐条）：
// 1) **汇编跳板（`.s`）不移植**。Go 用 `//go:cgo_import_dynamic` + `.s` 的 TEXT 跳板 + `syscall.syscall6`
//    调 CoreFoundation / CoreServices / libdispatch；C# 直接用 `DllImport` 声明同一批 C 符号
//    （见 FseventsDarwin.Ffi.cs）。`FSEventStreamCreate` 的 arch 专属跳板（把 latency 的 float64 从整数寄存器
//    搬到 xmm0、并硬编码 flags = 0x11）在 C# 里**完全不需要**：`double` 形参由 CLR 按平台 ABI 放进浮点寄存器，
//    flags 作为第 7 个显式实参传入（调用点写 `FseventsFfi.CreateFlags` = 0x11，与跳板硬编码值逐字节一致）。
// 2) GCD 回调的汇编实现 `fsEventsCallbackASM` → `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]`
//    的静态方法 `FseventsFfi.FsEventsCallbackNative`（Ffi 分片）。asm 存在的唯一理由是"不能进 Go ABI"；
//    C# 里 `[UnmanagedCallersOnly]` 就是那条通道，所以既没有跳板也不需要。函数地址用
//    `(IntPtr)(delegate* unmanaged[Cdecl]<…>)&方法` 取得（函数指针是 C# 语言级特性，**不需要 `unsafe`**；
//    本项目 AllowUnsafeBlocks 关闭，已用 net11.0 探针工程实测可编译）。
// 3) `FSEventStreamContext.info` 的语义：Go 传 `unsafe.Pointer(cb)`（asm 按 offset 0 读 eventPipeWrite）；
//    C# 传 `GCHandle.ToIntPtr(GCHandle.Alloc(cb))`，回调里 `GCHandle.FromIntPtr(info)` 取回 `StreamCallback`
//    再读它的 `_eventPipeWriteFd`。**托管对象地址不能交给原生代码**（引用类型 offset 0 是方法表指针，不是
//    fd），因此 info 从"状态对象的裸地址"变为"状态对象的 GCHandle"，Go 那条"结构体 offset 0 必须是
//    eventPipeWrite"的布局约束随之消失。Go 的 `runtime.Pinner.Pin(cb)`/`Unpin()` → `FseventsStream._pinner`
//    （`GCHandle.Alloc`/`Free`），生命周期相同：建流时 pin，teardown 完成后 unpin。
// 4) **管道同步方式**（与 Go 相同）：每流一条 `pipe(2)`。GCD 线程上的回调只做
//    「CFRetain(CFArray) + 拷贝 flags/ids + 把 payload 指针 `write(2)` 进管道」，业务分类全部发生在
//    eventLoop 线程（先 `read(2)` 出 payload 指针，再走 `FsEventsCallback`）。负载在**非托管内存**上跨线程
//    传递（Go 的 asm 用 malloc/memcpy；C# 用 `Marshal.AllocHGlobal` + libc `memcpy`），所有权随 write 成功
//    转移给 eventLoop，由 `FsEventsCallbackPayload.Close` 释放（= Go 的 `defer payload.close()`）。
// 5) Go 的 `go cb.eventLoop()`（goroutine 阻塞在 netpoll 上，空闲不占 OS 线程）→
//    `Task.Run(cb.EventLoop, TaskCreationOptions.LongRunning)`：C# 没有 netpoll 可挂，管道上的阻塞读放在普通
//    线程池线程上会饿死线程池，LongRunning 给它一条专属后台线程；"每流一个 eventLoop"的结构不变。
// 6) `atomic.Bool` → `volatile bool`（只有 Load/Store，无 RMW）；`atomic.Uintptr` 的 `Swap(0)` →
//    `Interlocked.Exchange(ref _stream, IntPtr.Zero)`（语义相同，见 `StopFSEventsStream`）。
// 7) Go 的 `switch { case <bool>: }` → `if / else if`；`flag&^uint32(ignoredFlags) == 0` →
//    `(flag & ~FsEventsFlags.Ignored) == 0`。
// 8) Go 的 `unix.Lstat(rawPath, &st) == nil`（只问"路径在不在"，lstat 不跟随符号链接）→ `FsEventsPathExists`：
//    先 `File.GetAttributes`（成功即存在），失败再退到 `FileInfo.LinkTarget`（断链符号链接本身也算存在，
//    与 lstat 一致）。这是本文件唯一没有一对一托管 API 的判定。
// 9) `sort.Strings` → `List<string>.Sort(StringComparer.Ordinal)`；`slices.BinarySearch` →
//    `Array.BinarySearch(路径数组, 值, StringComparer.Ordinal)`（同一个已排序数组、同一个序）。
// 10) 包级哨兵 `errCFStringCreateNull` 等（Go 共享实例）→ `FseventsErrors.Xxx()` 工厂（Watcher.cs 文件头第 1 条：
//     C# 异常不共享实例，否则首个调用点的栈会永久粘住）。`fmt.Errorf("…: %w", ErrOverflow)` 的三条 →
//     `FsWatchException.Overflow(…)`，消息文本按 Go 的 `err.Error()` 拼好（Kind = Overflow 已承载哨兵身份）。
//     Go 里"同一个 overflow 值发给多个 watch"（`setError(overflow)` 在 watch 循环外创建一次）→ C# 每个 watch
//     调用一次工厂（同上，异常实例不共享；Kind 相同，语义等价）。
// 11) Go 的 `cfStringEncodingUTF8` / `cfStringNormalizationFormC` 已由 Canonicalize.cs 的
//     `CoreFoundation.StringEncodingUtf8` / `CoreFoundation.NormalizationFormC` 提供（那份文件正是 fsevents ffi 里
//     `normalizeNFC`/`foldNativePath` 的落点），本文件只保留 FSEvents 自己的 flags 与 eventIDSinceNow。
// 12) Go 的 `fsEventsBackend.mu` 与内嵌 `watcherBase.mu` 是**两把不同的锁**；C# 继承来的字段就叫 `_mu`，
//     派生类再声明 `_mu` 会触发字段隐藏（CS0108，本仓 TreatWarningsAsErrors），故后端这把锁命名为
//     `_streamsMu`（加锁范围与顺序与 Go 完全一致）。
// 13) 本机是 win32，本后端**无法运行验证**，以下只能静态对照 Apple 头文件与 Go 汇编：
//     (a) FFI 签名/返回宽度（`FSEventStreamStart` 的 `Boolean` 是 uint8，C# 用 `byte` 与 Go 逐字节一致）；
//     (b) `FSEventStreamContext` = 40 字节（CFIndex version 8 + 4 个指针），amd64/arm64 相同；
//     (c) 回调 6 个形参的顺序（streamRef, info, numEvents, eventPaths, eventFlags, eventIds），以及
//         `kFSEventStreamCreateFlagUseCFTypes` 下 eventPaths 是 CFArrayRef；
//     (d) flags 位值（FSEvents.h）、`kFSEventStreamEventIdSinceNow`、`dispatch_sync_f` 的"等队列排空"语义；
//     (e) libSystem 符号名与 errno（EINTR = 4）、`pipe`/`fcntl(F_SETFD, FD_CLOEXEC)`/`write`/`memcpy`/`__error`；
//     (f) `GCHandle.FromIntPtr(info)` 在 GCD 线程上的可用性（托管代码可运行在任意 OS 线程上）、
//         `[UnmanagedCallersOnly]` 函数指针在 macOS amd64/arm64 上的 C 调用约定（CallConvCdecl = 平台默认 ABI）。
namespace TypeScript.FsWatch;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>Go: <c>fsevents_darwin.go</c> 的 FSEvents flag 位（来自 FSEvents.h）。</summary>
internal static class FsEventsFlags
{
    internal const uint MustScanSubDirs = 0x0000_0001;
    internal const uint UserDropped = 0x0000_0002;
    internal const uint KernelDropped = 0x0000_0004;
    internal const uint HistoryDone = 0x0000_0010;

    internal const uint ItemCreated = 0x0000_0100;
    internal const uint ItemRemoved = 0x0000_0200;
    internal const uint ItemInodeMetaMod = 0x0000_0400;
    internal const uint ItemRenamed = 0x0000_0800;
    internal const uint ItemModified = 0x0000_1000;
    internal const uint ItemFinderInfoMod = 0x0000_2000;
    internal const uint ItemChangeOwner = 0x0000_4000;
    internal const uint ItemXattrMod = 0x0000_8000;
    internal const uint ItemIsFile = 0x0001_0000;
    internal const uint ItemIsDir = 0x0002_0000;
    internal const uint ItemIsSymlink = 0x0004_0000;
    internal const uint ItemIsHardlink = 0x0010_0000;
    internal const uint ItemIsLastHardlink = 0x0020_0000;
    internal const uint ItemCloned = 0x0040_0000;

    /// <summary>Go: <c>const eventIDSinceNow = uint64(0xFFFFFFFFFFFFFFFF)</c>（kFSEventStreamEventIdSinceNow）。</summary>
    internal const ulong EventIdSinceNow = 0xFFFF_FFFF_FFFF_FFFF;

    /// <summary>Go: <c>const ignoredFlags</c>。这些位单独出现时不是内容变化（inode 元数据、类型、克隆标记）。</summary>
    internal const uint Ignored = ItemIsHardlink | ItemIsLastHardlink | ItemIsSymlink | ItemIsDir | ItemIsFile | ItemCloned;
}

/// <summary>Go: <c>type fseventsState struct</c>。每个 dirWatch 的 FSEvents 簿记。</summary>
internal sealed class FseventsState
{
    /// <summary>Go: <c>terminated atomic.Bool</c>。true = 该 watch 已终止（根被删），不再参与事件路由。</summary>
    internal volatile bool _terminated;
}

/// <summary>Go: <c>type fseventsWatchSnapshot struct</c>。建流时对一个 dirWatch 的快照。</summary>
internal sealed class FseventsWatchSnapshot
{
    internal DirWatch _w = null!;
    internal FseventsState _state = null!;
}

/// <summary>Go: <c>type fseventsStream struct</c>。一条 FSEventStream 及其回调基础设施。</summary>
internal sealed class FseventsStream
{
    /// <summary>Go: <c>stream atomic.Uintptr</c>。0 = 已拆掉（Swap 由 <see cref="FSEventsBackend.StopFSEventsStream"/> 独占）。</summary>
    internal IntPtr _stream;

    /// <summary>Go: <c>cb *streamCallback</c>。</summary>
    internal StreamCallback? _cb;

    /// <summary>Go: <c>pinner runtime.Pinner</c> → GCHandle（见文件头第 3 条）。</summary>
    internal GCHandle _pinner;
}

/// <summary>Go: <c>func([]string, []fseventsWatchSnapshot) (*fseventsStream, error)</c>（startFSEventsStreams 的形参）。</summary>
internal delegate FseventsStream? FseventsStartStreamFunc(string[] paths, FseventsWatchSnapshot[] watches);

/// <summary>Go: <c>fsevents_darwin.go</c> 的包级哨兵错误（本文件只用到这几个）。</summary>
internal static class FseventsErrors
{
    /// <summary>Go: <c>errCFStringCreateNull</c>。</summary>
    internal static Exception CfStringCreateNull() => new InvalidOperationException("CFStringCreate returned NULL");

    /// <summary>Go: <c>errCFArrayCreateNull</c>。</summary>
    internal static Exception CfArrayCreateNull() => new InvalidOperationException("CFArrayCreate returned NULL");

    /// <summary>Go: <c>errStreamCreateNull</c>。</summary>
    internal static Exception StreamCreateNull() => new InvalidOperationException("FSEventStreamCreate returned NULL");

    /// <summary>Go: <c>errStreamStartFailed</c>。</summary>
    internal static Exception StreamStartFailed() => new InvalidOperationException("error starting FSEvents stream");

    /// <summary>Go: <c>errFSEventsUserDropped</c> = <c>fmt.Errorf("events were dropped by the FSEvents client: %w", ErrOverflow)</c>。</summary>
    internal static FsWatchException UserDropped() =>
        FsWatchException.Overflow("events were dropped by the FSEvents client: fswatch: event overflow; some changes were missed");

    /// <summary>Go: <c>errFSEventsKernelDropped</c>。</summary>
    internal static FsWatchException KernelDropped() =>
        FsWatchException.Overflow("events were dropped by the kernel: fswatch: event overflow; some changes were missed");

    /// <summary>Go: <c>errFSEventsTooMany</c>。</summary>
    internal static FsWatchException TooMany() =>
        FsWatchException.Overflow("too many events: fswatch: event overflow; some changes were missed");

    /// <summary>Go: 四处 `fmt.Errorf("%w: watched directory removed", ErrWatchTerminated)`（每次都是新值）。</summary>
    internal static FsWatchException WatchedDirectoryRemoved() =>
        FsWatchException.WatchTerminated("fswatch: watch terminated: watched directory removed");
}

/// <summary>
/// Go: <c>type fsEventsBackend struct</c>（嵌入 watcherBase）。macOS 的 FSEvents 后端：没有事件循环线程，
/// <c>start()</c> 只报就绪；<c>subscribe</c>/<c>closeWatch</c> 重建共享的流集合。
/// </summary>
internal sealed class FSEventsBackend : WatcherBase, ISubscribeManyImpl
{
    /// <summary>Go: <c>const fseventsPathsPerStream = 512</c>。单流建失败后按这个粒度分块退化。</summary>
    private const int _pathsPerStream = 512;

    /// <summary>Go: <c>func init() { fseventsWatcher.factory = …; fseventsWatcher.sequence = … }</c>。</summary>
    [SuppressMessage("Usage", "CA2255", Justification = "Go 的 fsevents_darwin.go init()：模块初始化器等价于「包被加载即注册」（先例见 Windows.cs 文件头第 9 条）。")]
    [ModuleInitializer]
    internal static void InitializeFSEventsWatcher()
    {
        if (OperatingSystem.IsMacOS())
        {
            FsWatch.FSEventsWatcherInstance.Factory = static () => new FSEventsBackend();
            FsWatch.FSEventsWatcherInstance.Sequence = static () => FseventsFfi.FsEventsGetCurrentEventId();
        }
    }

    /// <summary>Go: <c>fsEventsBackend.mu</c>（与 watcherBase.mu 是两把锁，见文件头第 12 条）。</summary>
    private readonly object _streamsMu = new();

    /// <summary>Go: <c>watches map[*dirWatch]*fseventsState</c>。</summary>
    private readonly Dictionary<DirWatch, FseventsState> _watches = new();

    /// <summary>Go: <c>streams []*fseventsStream</c>。当前共享的流集合（元素可能为 null，见 Go 的 nil 元素）。</summary>
    private FseventsStream?[] _streams = [];

    internal FSEventsBackend()
    {
        Init(this);
    }

    /// <summary>Go: <c>func (b *fsEventsBackend) start() error</c>。FSEvents 没有共享事件循环要启动。</summary>
    public override void Start() => NotifyStarted();

    /// <summary>Go: <c>func checkWatcher(w *dirWatch) error</c>。订阅前确认物理路径存在且是目录。</summary>
    private static void CheckWatcher(DirWatch w)
    {
        FileAttributes attrs;
        try
        {
            // Go: os.Stat(w.physicalDir)（跟随符号链接）。
            attrs = File.GetAttributes(w._physicalDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or NotSupportedException)
        {
            throw new DirWatchError(ex, w);
        }

        if (!attrs.HasFlag(FileAttributes.Directory))
        {
            // Go: syscall.ENOTDIR。文案与 WalkDir.cs/Windows.cs 的同类判定一致。
            throw new DirWatchError(new IOException($"{w._physicalDir}: not a directory"), w);
        }
    }

    /// <summary>Go: <c>func (b *fsEventsBackend) activeWatchesLocked() []fseventsWatchSnapshot</c>（调用方持锁）。</summary>
    private FseventsWatchSnapshot[] ActiveWatchesLocked()
    {
        var watches = new List<FseventsWatchSnapshot>(_watches.Count);
        foreach (var (w, state) in _watches)
        {
            if (state._terminated)
            {
                continue;
            }

            watches.Add(new FseventsWatchSnapshot { _w = w, _state = state });
        }

        return [.. watches];
    }

    /// <summary>Go: <c>func (b *fsEventsBackend) startStreams(watches []fseventsWatchSnapshot) ([]*fseventsStream, error)</c>。</summary>
    private FseventsStream?[] StartStreams(FseventsWatchSnapshot[] watches) => StartFSEventsStreams(watches, StartStream);

    /// <summary>
    /// Go: <c>func startFSEventsStreams(watches []fseventsWatchSnapshot, startStream func([]string, []fseventsWatchSnapshot) (*fseventsStream, error)) ([]*fseventsStream, error)</c>。
    /// 先用一条流包住全部路径；单流建失败时按 <see cref="_pathsPerStream"/> 分块退化为多条流。
    /// </summary>
    internal static FseventsStream?[] StartFSEventsStreams(FseventsWatchSnapshot[] watches, FseventsStartStreamFunc startStream)
    {
        if (watches.Length == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(watches.Length);
        var paths = new List<string>(watches.Length);
        foreach (var watch in watches)
        {
            var path = watch._w._physicalDir;
            if (!seen.Add(path))
            {
                continue;
            }

            paths.Add(path);
        }

        paths.Sort(StringComparer.Ordinal); // Go: sort.Strings（逐字节序）
        var allPaths = paths.ToArray();

        try
        {
            return [startStream(allPaths, watches)];
        }
        catch (Exception)
        {
            // 单流失败 → 落到下面的分块路径（Go 里 err != nil 时不返回，继续走 chunks）。
        }

        var streams = new List<FseventsStream?>((allPaths.Length + _pathsPerStream - 1) / _pathsPerStream);
        for (var offset = 0; offset < allPaths.Length; offset += _pathsPerStream)
        {
            var chunkLength = Math.Min(allPaths.Length - offset, _pathsPerStream);
            var chunkPaths = new string[chunkLength];
            Array.Copy(allPaths, offset, chunkPaths, 0, chunkLength);

            FseventsStream? stream;
            try
            {
                stream = startStream(chunkPaths, WatchesForFSEventsPaths(watches, chunkPaths));
            }
            catch (Exception)
            {
                StopFSEventsStreams([.. streams]);
                throw;
            }

            streams.Add(stream);
        }

        return [.. streams];
    }

    /// <summary>Go: <c>func watchesForFSEventsPaths(watches []fseventsWatchSnapshot, paths []string) []fseventsWatchSnapshot</c>。</summary>
    private static FseventsWatchSnapshot[] WatchesForFSEventsPaths(FseventsWatchSnapshot[] watches, string[] paths)
    {
        if (paths.Length == 0)
        {
            return [];
        }

        var filtered = new List<FseventsWatchSnapshot>(watches.Length);
        foreach (var watch in watches)
        {
            // Go: slices.BinarySearch(paths, watch.w.physicalDir)（paths 已排序）。
            if (Array.BinarySearch(paths, watch._w._physicalDir, StringComparer.Ordinal) >= 0)
            {
                filtered.Add(watch);
            }
        }

        return [.. filtered];
    }

    /// <summary>Go: <c>func (b *fsEventsBackend) startStream(paths []string, watches []fseventsWatchSnapshot) (*fseventsStream, error)</c>。</summary>
    private FseventsStream? StartStream(string[] paths, FseventsWatchSnapshot[] watches)
    {
        if (paths.Length == 0)
        {
            return null;
        }

        // CFArrayCreate 的 callbacks 参数是 NULL → 数组**不 retain** 元素，因此 cfStrings 必须活到
        // FSEventStreamCreate 返回之后才释放（Go 用 defer 达到同样效果）。下面 finally 的顺序相同：
        // 先 release CFArray，再 release 每个 CFString。
        var cfStrings = new List<IntPtr>(paths.Length);
        var pathsToWatch = IntPtr.Zero;
        try
        {
            foreach (var path in paths)
            {
                // Go: cfStringCreate(0, &dirCStr[0], cfStringEncodingUTF8)（cstr 是 NUL 结尾的 UTF-8 字节）。
                var cfDir = CoreFoundation.StringCreate(path);
                if (cfDir == 0)
                {
                    throw FseventsErrors.CfStringCreateNull();
                }

                cfStrings.Add(cfDir);
            }

            var values = Marshal.AllocHGlobal(IntPtr.Size * cfStrings.Count);
            try
            {
                for (var i = 0; i < cfStrings.Count; i++)
                {
                    Marshal.WriteIntPtr(values, i * IntPtr.Size, cfStrings[i]);
                }

                // Go: cfArrayCreate(0, &cfStrings[0], len(cfStrings), 0)（kCFTypeArrayCallBacks 由 0 → NULL 表示不 retain）。
                pathsToWatch = FseventsFfi.CFArrayCreate(IntPtr.Zero, values, cfStrings.Count, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(values);
            }

            if (pathsToWatch == 0)
            {
                throw FseventsErrors.CfArrayCreateNull();
            }

            var cb = StreamCallback.New(watches);
            var state = new FseventsStream { _cb = cb };
            try
            {
                state._pinner = GCHandle.Alloc(cb); // Go: state.pinner.Pin(cb)

                var ctx = new FseventStreamContext
                {
                    Version = 0,
                    Info = GCHandle.ToIntPtr(state._pinner), // Go: uintptr(unsafe.Pointer(cb))，见文件头第 3 条
                    Retain = IntPtr.Zero,
                    Release = IntPtr.Zero,
                    CopyDescription = IntPtr.Zero,
                };

                var stream = FseventsFfi.FSEventStreamCreate(
                    IntPtr.Zero,
                    FseventsFfi.FsEventsCallbackPointer,
                    in ctx,
                    pathsToWatch,
                    FsEventsFlags.EventIdSinceNow,
                    0.001, // Go: 0.001 秒的延迟窗口
                    FseventsFfi.CreateFlags); // 跳板里硬编码的 0x11，见文件头第 1 条

                if (stream == 0)
                {
                    throw FseventsErrors.StreamCreateNull();
                }

                FseventsFfi.FSEventStreamSetDispatchQueue(stream, cb._queue);
                if (FseventsFfi.FSEventStreamStart(stream) == 0)
                {
                    FseventsFfi.FSEventStreamInvalidate(stream);
                    FseventsFfi.FSEventStreamRelease(stream);
                    throw FseventsErrors.StreamStartFailed();
                }

                FseventsFfi.FSEventStreamFlushSync(stream);
                state._stream = stream;
                return state;
            }
            catch (Exception)
            {
                // Go 的两处收尾（create 返回 NULL / start 失败）：cb.close(); state.cb = nil; state.pinner.Unpin()
                // （上面那条 start 失败分支在抛之前已经 Invalidate + Release 过流）。
                cb.Close();
                state._cb = null;
                if (state._pinner.IsAllocated)
                {
                    state._pinner.Free();
                }

                throw;
            }
        }
        finally
        {
            if (pathsToWatch != 0)
            {
                CoreFoundation.Release(pathsToWatch);
            }

            foreach (var cfString in cfStrings)
            {
                CoreFoundation.Release(cfString);
            }
        }
    }

    /// <summary>
    /// Go: <c>func teardownStream(stream uintptr, cb *streamCallback)</c>。Stop 与 Invalidate 阻止新回调，
    /// <c>waitDispatchQueue</c> 等已排在该流串行队列上的回调跑完，最后 <c>cb.close()</c> 收掉 Go 的事件循环。
    /// </summary>
    private static void TeardownStream(IntPtr stream, StreamCallback? cb)
    {
        FseventsFfi.FSEventStreamStop(stream);
        if (cb is not null)
        {
            FseventsFfi.FSEventStreamInvalidate(stream);
            cb.WaitDispatchQueue();
            cb.Close();
        }
        else
        {
            FseventsFfi.FSEventStreamInvalidate(stream);
        }

        FseventsFfi.FSEventStreamRelease(stream);
    }

    /// <summary>Go: <c>func stopFSEventsStreams(streams []*fseventsStream)</c>。</summary>
    private static void StopFSEventsStreams(FseventsStream?[] streams)
    {
        foreach (var stream in streams)
        {
            StopFSEventsStream(stream);
        }
    }

    /// <summary>
    /// Go: <c>func stopFSEventsStream(state *fseventsStream)</c>。atomic Swap 让重复/并发调用里只有一个执行清理。
    /// </summary>
    private static void StopFSEventsStream(FseventsStream? state)
    {
        if (state is null)
        {
            return;
        }

        var stream = Interlocked.Exchange(ref state._stream, IntPtr.Zero); // Go: state.stream.Swap(0)
        if (stream == IntPtr.Zero)
        {
            return;
        }

        var cb = state._cb;
        TeardownStream(stream, cb);
        state._cb = null;
        if (state._pinner.IsAllocated)
        {
            state._pinner.Free(); // Go: state.pinner.Unpin()
        }
    }

    /// <summary>Go: <c>func (b *fsEventsBackend) subscribe(w *dirWatch) error</c>。</summary>
    public override void Subscribe(DirWatch w) => SubscribeMany([w]);

    /// <summary>Go: <c>func (b *fsEventsBackend) subscribeMany(watchesToAdd []*dirWatch) error</c>。</summary>
    public void SubscribeMany(DirWatch[] watchesToAdd)
    {
        if (watchesToAdd.Length == 0)
        {
            return;
        }

        var states = new Dictionary<DirWatch, FseventsState>(watchesToAdd.Length);
        foreach (var w in watchesToAdd)
        {
            CheckWatcher(w);
            states[w] = new FseventsState();
        }

        FseventsWatchSnapshot[] watches;
        lock (_streamsMu)
        {
            foreach (var (w, state) in states)
            {
                w._state = state;
                _watches[w] = state;
            }

            watches = ActiveWatchesLocked();
        }

        FseventsStream?[] streams;
        try
        {
            streams = StartStreams(watches);
        }
        catch (Exception err)
        {
            lock (_streamsMu)
            {
                foreach (var (w, state) in states)
                {
                    // Go: if b.watches[w] == state（引用比较）
                    if (_watches.TryGetValue(w, out var current) && ReferenceEquals(current, state))
                    {
                        _watches.Remove(w);
                        w._state = null;
                    }
                }
            }

            throw new DirWatchError(err, watchesToAdd[0]);
        }

        FseventsStream?[] oldStreams;
        lock (_streamsMu)
        {
            oldStreams = _streams;
            _streams = streams;
        }

        StopFSEventsStreams(oldStreams);
    }

    /// <summary>Go: <c>func (b *fsEventsBackend) closeWatch(w *dirWatch) error</c>。</summary>
    public override void CloseWatch(DirWatch w)
    {
        var state = w._state as FseventsState;
        w._state = null;
        if (state is null)
        {
            return;
        }

        state._terminated = true;

        FseventsWatchSnapshot[] watches;
        lock (_streamsMu)
        {
            _watches.Remove(w);
            watches = ActiveWatchesLocked();
        }

        var streams = StartStreams(watches);

        FseventsStream?[] oldStreams;
        lock (_streamsMu)
        {
            oldStreams = _streams;
            _streams = streams;
        }

        StopFSEventsStreams(oldStreams);
    }

    /// <summary>
    /// Go: <c>func fsEventsCallback(cb *streamCallback, payload *fsEventsCallbackPayload)</c>。在 streamCallback 的
    /// eventLoop 线程上运行（**不是** GCD 线程、不是调用方线程）：分类一批事件并路由到匹配的 dirWatch 事件列表。
    /// </summary>
    /// <param name="cb">拥有该批事件的流。</param>
    /// <param name="payloadPointer">非托管内存里的 <c>fsEventsCallbackPayload</c> 指针（0 = 空负载）。</param>
    internal static void FsEventsCallback(StreamCallback cb, IntPtr payloadPointer)
    {
        var payload = FsEventsCallbackPayload.Read(payloadPointer);
        try
        {
            if (payload._self == IntPtr.Zero || payload._paths == IntPtr.Zero || payload._flags == IntPtr.Zero || payload._ids == IntPtr.Zero)
            {
                return;
            }

            var numEvents = payload._numEvents;
            var watches = cb._watches;
            var touched = new HashSet<DirWatch>();

            for (nuint i = 0; i < numEvents; i++)
            {
                // Go: *(*uint32)(unsafe.Add(nil, flags+i*flagSize)) / *(*uint64)(unsafe.Add(nil, ids+i*idSize))
                var flag = (uint)Marshal.ReadInt32(payload._flags, (int)(i * 4));
                var eventID = (ulong)Marshal.ReadInt64(payload._ids, (int)(i * 8));
                var pathRef = FseventsFfi.CFArrayGetValueAtIndex(payload._paths, (nint)i);
                var path = CoreFoundation.StringToNFC(pathRef);
                if (path.Length == 0)
                {
                    continue;
                }

                var comparison = new ComparisonPath { Path = path };

                var isRemoved = (flag & FsEventsFlags.ItemRemoved) != 0;
                var isRenamed = (flag & FsEventsFlags.ItemRenamed) != 0;
                var isCreated = (flag & FsEventsFlags.ItemCreated) != 0;
                var isDone = (flag & FsEventsFlags.HistoryDone) != 0;

                if ((flag & FsEventsFlags.MustScanSubDirs) != 0)
                {
                    foreach (var watch in watches)
                    {
                        if (watch._state._terminated)
                        {
                            continue;
                        }

                        if (FseventsOverflowMatchesPrepared(watch._w, ref comparison))
                        {
                            watch._w._events.SetError(FseventsOverflowError(flag));
                            touched.Add(watch._w);
                        }
                    }
                }

                if (isDone)
                {
                    break;
                }

                if ((flag & ~FsEventsFlags.Ignored) == 0)
                {
                    continue;
                }

                var rawPath = path;
                var pathExists = false;
                var pathExistsKnown = false;

                foreach (var watch in watches)
                {
                    if (watch._state._terminated)
                    {
                        continue;
                    }

                    var w = watch._w;
                    if (!FseventsDisplayPathPrepared(w, ref comparison, out var displayPath))
                    {
                        continue;
                    }

                    // 被监听目录自身的元数据变化不投递：子项增删会让 fseventsd 在被监听目录上报一次变化，而订阅者
                    // 关心的是目录**内部**的变化。目录被删除时仍然要投递（Watcher 依赖它拆流）。
                    if (displayPath == w._dir && !isRemoved && !isRenamed)
                    {
                        continue;
                    }

                    if (isRemoved && !isCreated)
                    {
                        if (displayPath == w._dir)
                        {
                            w._events.RemoveWatchRootAt(displayPath, eventID);
                        }
                        else
                        {
                            w._events.RemoveAt(displayPath, eventID);
                        }

                        if (w.TerminateCallbacksForDeletedRoot(displayPath, eventID, FseventsErrors.WatchedDirectoryRemoved()))
                        {
                            touched.Add(w);
                        }

                        if (displayPath == w._dir)
                        {
                            watch._state._terminated = true;
                            w._events.SetError(FseventsErrors.WatchedDirectoryRemoved());
                        }
                    }
                    else if (isRenamed || (isRemoved && isCreated))
                    {
                        // 内核会把部分删除报成 rename：这里做一次存在性确认（每个事件最多一次）。
                        if (!pathExistsKnown)
                        {
                            pathExists = FsEventsPathExists(rawPath);
                            pathExistsKnown = true;
                        }

                        if (pathExists)
                        {
                            if (displayPath == w._dir)
                            {
                                w._events.UpdateWatchRootAt(displayPath, eventID);
                            }
                            else
                            {
                                w._events.UpdateAt(displayPath, eventID);
                            }
                        }
                        else
                        {
                            if (displayPath == w._dir)
                            {
                                w._events.RemoveWatchRootAt(displayPath, eventID);
                            }
                            else
                            {
                                w._events.RemoveAt(displayPath, eventID);
                            }

                            if (w.TerminateCallbacksForDeletedRoot(displayPath, eventID, FseventsErrors.WatchedDirectoryRemoved()))
                            {
                                touched.Add(w);
                            }

                            if (displayPath == w._dir)
                            {
                                watch._state._terminated = true;
                                w._events.SetError(FseventsErrors.WatchedDirectoryRemoved());
                            }
                        }
                    }
                    else
                    {
                        if (displayPath == w._dir)
                        {
                            w._events.UpdateWatchRootAt(displayPath, eventID);
                        }
                        else
                        {
                            w._events.UpdateAt(displayPath, eventID);
                        }
                    }

                    touched.Add(w);
                }
            }

            foreach (var w in touched)
            {
                w.Notify();
            }
        }
        finally
        {
            payload.Close(); // Go: defer payload.close()
        }
    }

    /// <summary>
    /// Go: <c>switch { case flag&amp;flagUserDropped != 0: … case flag&amp;flagKernelDropped != 0: … default: … }</c>。
    /// Go 在 watch 循环外只求值一次并共享同一个 error 值；C# 每个受影响 watch 新建一个实例（见文件头第 10 条）。
    /// </summary>
    private static FsWatchException FseventsOverflowError(uint flag)
    {
        if ((flag & FsEventsFlags.UserDropped) != 0)
        {
            return FseventsErrors.UserDropped();
        }

        if ((flag & FsEventsFlags.KernelDropped) != 0)
        {
            return FseventsErrors.KernelDropped();
        }

        return FseventsErrors.TooMany();
    }

    /// <summary>Go: <c>func fseventsDisplayPath(w *dirWatch, rawPath string) (string, bool)</c>（Go 测试直接调用）。</summary>
    internal static bool FseventsDisplayPath(DirWatch w, string rawPath, out string displayPath)
    {
        var path = new ComparisonPath { Path = rawPath };
        return FseventsDisplayPathPrepared(w, ref path, out displayPath);
    }

    /// <summary>
    /// Go: <c>func fseventsDisplayPathPrepared(w *dirWatch, rawPath *comparisonPath) (string, bool)</c>。
    /// 把 FSEvents 报的物理路径重定基到调用方可见路径；命中时返回 true。
    /// </summary>
    private static bool FseventsDisplayPathPrepared(DirWatch w, ref ComparisonPath rawPath, out string displayPath)
    {
        var physical = new ComparisonPath
        {
            Path = w._physicalDir,
            Folded = w._physicalDirFold,
            Ready = w._physicalDirFold.Length != 0,
        };

        if (w._comparer.RebasePrepared(ref rawPath, in physical, w._dir, out displayPath))
        {
            return true;
        }

        if (w._physicalDir != w._dir)
        {
            var logical = new ComparisonPath
            {
                Path = w._dir,
                Folded = w._dirFold,
                Ready = w._dirFold.Length != 0,
            };

            return w._comparer.RebasePrepared(ref rawPath, in logical, w._dir, out displayPath);
        }

        displayPath = "";
        return false;
    }

    /// <summary>Go: <c>func fseventsOverflowMatches(w *dirWatch, rawPath string) bool</c>（Go 测试直接调用）。</summary>
    internal static bool FseventsOverflowMatches(DirWatch w, string rawPath)
    {
        var path = new ComparisonPath { Path = rawPath };
        return FseventsOverflowMatchesPrepared(w, ref path);
    }

    /// <summary>
    /// Go: <c>func fseventsOverflowMatchesPrepared(w *dirWatch, rawPath *comparisonPath) bool</c>。
    /// 溢出事件没有具体子路径，只能靠"物理根 / 逻辑根与事件路径互为父亲或祖先"来判定归属。
    /// </summary>
    private static bool FseventsOverflowMatchesPrepared(DirWatch w, ref ComparisonPath rawPath)
    {
        var physical = new ComparisonPath
        {
            Path = w._physicalDir,
            Folded = w._physicalDirFold,
            Ready = w._physicalDirFold.Length != 0,
        };

        if (w._comparer.SuffixPrepared(in physical, ref rawPath, out _))
        {
            return true;
        }

        if (w._comparer.SuffixPrepared(in rawPath, ref physical, out _))
        {
            return true;
        }

        if (w._physicalDir != w._dir)
        {
            var logical = new ComparisonPath
            {
                Path = w._dir,
                Folded = w._dirFold,
                Ready = w._dirFold.Length != 0,
            };

            if (w._comparer.SuffixPrepared(in logical, ref rawPath, out _))
            {
                return true;
            }

            return w._comparer.SuffixPrepared(in rawPath, ref logical, out _);
        }

        return false;
    }

    /// <summary>Go: <c>unix.Lstat(rawPath, &amp;st) == nil</c>（见文件头第 8 条）。</summary>
    private static bool FsEventsPathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // 目标不存在：断链符号链接本身仍然"存在"（lstat 成功），用 LinkTarget 兜住这一情形。
            try
            {
                return new FileInfo(path).LinkTarget is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or NotSupportedException)
        {
            return false;
        }
    }
}
