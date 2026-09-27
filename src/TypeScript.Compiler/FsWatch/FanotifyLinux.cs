// ported from: internal/fswatch/fanotify_linux.go:1-770
//
// C# 映射与偏离（逐条）：
// 1) Go 用 `golang.org/x/sys/unix`。其中 fanotify_init / fanotify_mark / name_to_handle_at / statfs 在 x/sys 里
//    是**裸 syscall**（`Syscall(SYS_FANOTIFY_INIT, …)`、`Syscall6(SYS_NAME_TO_HANDLE_AT, …)` 等），C# 侧同样走
//    libc 的 `syscall(2)`，syscall 号与常量逐个抄自 x/sys v0.48.0（go.mod 里的版本）的 zerrors_linux.go /
//    zsysnum_linux_<arch>.go（见 `Syscalls` 表与文件末尾的位值常量）。
//    **偏离**：C# 无法声明真正的 varargs（`__arglist` 会污染调用点且本仓无先例），故把 `long syscall(long n, …)`
//    按实参个数拆成 4 个非 varargs 的 P/Invoke（EntryPoint 同为 "syscall"）：`Syscall2/3/5/6`。x86-64 / aarch64 /
//    i386 / arm / riscv64 / loong64 / ppc64le / s390x 的 libc `syscall` 实现只按参数寄存器（或栈槽）取整型实参，
//    **不读 AL（SSE 寄存器计数）**，而本文件从不传浮点实参，故"固定原型调用 varargs 函数"在此完全等价。
//    libc 的错误约定：返回 -1 并置 errno（`Marshal.GetLastPInvokeError()`）。
//    其余调用（pipe2 / poll / read / write / close / lstat）用 libc 同名符号——这些在 glibc 与 musl 上都稳定存在
//    （相反 fanotify_mark 在 glibc 的 syscalls.list 里没有条目，所以 fanotify 那两个调用绝不能绑 libc 符号）。
// 2) Go 的 `(*unix.FanotifyEventMetadata)(unsafe.Pointer(&data[0]))` 结构体重解释 → C# 逐字段 `Marshal.Read*`。
//    **结构体偏移表**（这些布局在全部受支持的 Linux 架构上一致，因为成员都是固定宽度且 `__aligned_u64` 在
//    offset 8 上本来就自然对齐）：
//      struct fanotify_event_metadata（24 字节 = unix.SizeofFanotifyEventMetadata）：
//        event_len  u32  +0
//        vers       u8   +4
//        reserved   u8   +5   （Go 不读）
//        metadata_len u16 +6
//        mask       u64  +8   （uapi 里是 `__aligned_u64 mask`）
//        fd         s32  +16
//        pid        s32  +20  （Go 不读）
//      struct fanotify_event_info_header（4 字节）：info_type u8 +0, pad u8 +1, len u16 +2
//      struct fanotify_event_info_fid：hdr(4) → fsid(8) → `struct file_handle`
//      struct file_handle：handle_bytes u32 +0, handle_type s32 +4, f_handle[] +8（头长 8）
//        —— Go 的 `unix.FileHandle.Type()` 取 +4，`Bytes()` 从 +8 开始（`unsafe.Add(&Type, 4)`）。
// 3) struct statfs / statfs64 只用 fsid 那 8 字节（`__kernel_fsid_t { int val[2]; }` = Go 的 `st.Fsid.Val`）。
//    **f_fsid 偏移表**（Go 的 `unix.Statfs` 在 x/sys 里按架构选了不同的 syscall，本文件照抄）：
//      x86-64(137) / arm64(43) / riscv64(43) / loong64(43) / ppc64le(99)：SYS_statfs(path, buf)，
//        f_type +0、f_bsize +8（各 8 字节）、f_blocks +16 … f_ffree +48，**f_fsid +56**，缓冲 120 字节。
//      s390x(99)：SYS_statfs(path, buf)，但 f_type +0 / f_bsize +4 各只有 4 字节（s390 的 uapi 如此），
//        f_blocks +8 … f_ffree +40，**f_fsid +48**，缓冲 88 字节。
//      i386(268) / arm(266)：SYS_statfs64(path, sizeof(struct statfs64), buf)，f_type +0 / f_bsize +4 各 4 字节，
//        f_blocks +8 … f_ffree +40，**f_fsid +48**；sizeof = 84(i386，u64 按 4 对齐) / 88(arm，u64 按 8 对齐后
//        结构体尾补 4 字节)——与 Go 的 `unsafe.Sizeof(Statfs_t{})` 一致（Go 的 arm Statfs_t 末尾就有 `_ [4]byte`）。
//      （缓冲一律 `Marshal.AllocHGlobal(128)`，比最大的 120 还大，避免任何架构上结构性越界写。）
// 4) **不使用 `unsafe`**（本仓未开 AllowUnsafeBlocks）：所有非托管内存都是 `Marshal.AllocHGlobal` 出来的，
//    字符串进出用 `Marshal.StringToCoTaskMemUTF8` / `Marshal.PtrToStringUTF8`，结构体按上面的偏移表逐字段读写。
// 5) Go 的键 `fanotifyHandleKey.handle` 是 `string(handleBytes)`（原始字节直接当 string，比较是逐字节的）
//    → C# 的 `ReadHandleKey` 用 Latin-1 把每个字节映射成一个 char。**不能**用 `Marshal.PtrToStringUTF8`：它会把
//    非法 UTF-8 序列折叠成 U+FFFD，让两个不同的文件句柄变成同一个键（`[0xFF]` 与 `[0xFE]` 会撞）。
// 6) 事件里的子项名字 `dfidDfidName.name`：Go 的 `string(nameData)`（遇到首个 NUL 截断，**保留**原始字节）→
//    C# 的 `ReadNulTerminatedUtf8`（同样在 NUL 处截断，但把非法 UTF-8 序列替换成 U+FFFD）。C# 的路径必须是合法
//    UTF-16 字符串，这与 InotifyLinux.cs 文件头第 4 条采用的处理一致。
// 7) Go 的 `map[fanotifyHandleKey][]*fanotifySubscription` → `Dictionary<FanotifyHandleKey, List<FanotifySubscription>>`；
//    `map[*dirWatch]struct{}` → `HashSet<DirWatch>`（引用相等）；`fanotifyHandleKey` 是要当字典键用的值类型
//    → C# `readonly record struct`（成员含 string，`Equals`/`GetHashCode` 走值语义，等价 Go 的逐字段可比较性）。
// 8) Go 的 `pipeWriteFD atomic.Int32`（让 shutdown 能安全对抗 start 线程的 deferred closeFDs）→ 普通字段
//    `_pipeWriteFD` + `Volatile.Read` / `Interlocked.Exchange`（= Go 的 Load / Swap）。构造函数里的 `Store(-1)`
//    由字段初始值 `-1` 承担。
// 9) Go 的 `chan struct{} endedSignal`（close = 就绪）→ `TaskCompletionSource`；`close(b.endedSignal)` →
//    `TrySetResult()`（幂等，Go 二次 close 会 panic，这里不会）。
// 10) Go 的 `defer func() { b.closeFDs(); close(b.endedSignal) }()` → `Start()` 的 `try/finally`，顺序一致
//     （先关 fd、再放行 shutdown 的等待者）。pipe2 失败时同样**不**注册清理（Go 的 defer 在 pipe2 成功之后）。
// 11) Go 的 `kept := list[:0]` + append 是**原地压缩**切片 → C# 用同样的写指针原地压缩（`list[write++] = s` +
//     `RemoveRange`），不额外分配。但 C# 不允许在 `foreach` 期间改 `List`，所以"遍历订阅表时可能被压缩"的两处
//     （handleRenameEvent/handleParsedEvent）先取快照数组（`SubsFor`）。唯一语义差异：Go 的 `range` 在切片被
//     原地压缩时可能重复处理被搬到尾部的元素，C# 每个订阅只处理一次。
// 12) Go 在 `range map` 的同时 delete/改写该 map 是合法的 → C# 先 `new List<Key>(_subscriptions.Keys)` 快照键，
//     值的读写仍落在同一个字典上（最终状态与 Go 相同）。
// 13) errno：Linux 上 EWOULDBLOCK == EAGAIN == 11、EOPNOTSUPP == ENOTSUP == 95，故 Go 的多重判定各自退化成
//     单次比较。Go 的 `errors.Is(err, unix.EX)` → C# 直接比较 `Marshal.GetLastPInvokeError()` 的返回值。
//     错误承载类型沿用 InotifyLinux.cs 的约定：裸 errno → `IOException(strerror, Win32Exception(errno))`
//     （`Win32Exception.NativeErrorCode` 就是 errno），`fmt.Errorf("…: %w", err)` → 消息拼接 + InnerException。
// 14) `maybeWrapUnsupportedFilesystem`：Go 是 `fmt.Errorf("%w: %w", err, ErrFilesystemUnsupported)`（Go 1.20 的
//     多错误包装）→ C# 让**外层**异常直接带 `FsWatchErrorKind.FilesystemUnsupported`、InnerException 挂 errno 错误
//     （消息与 Go 一致：`<err>: fswatch: watcher backend unsupported on this filesystem`）。这样
//     `FsWatchException.Is(err, FsWatchErrorKind.FilesystemUnsupported)`（FallbackWatcher 的路由谓词）照样命中。
// 15) `ErrOverflow` → `FsWatchException.Overflow()`；`fmt.Errorf("%w: watched directory removed", ErrWatchTerminated)`
//     → `FsWatchException.WatchTerminated("fswatch: watch terminated: watched directory removed")`。**不共享实例**
//     （C# 异常抛出时记录栈，跨抛复用同一实例会永久粘住首个调用点的栈）。
// 16) Go 的 `_ = walkDir(...)` / `_ = b.markDir(...)`（显式丢弃错误）→ C# `try { … } catch { }`，逐处注明；
//     `subscribe` 里的 `_ = b.closeWatch(w)` 同理。
// 17) Go 的 `unix.Lstat(path, &st) != nil`（合并事件里的"路径是否还在"探测）只用到**返回值** → C# 用 libc
//     `lstat` + 每次调用分配 256 字节非托管缓冲（`struct stat` 的布局随架构/ABI 变化，这里一个字段都不读，
//     给足余量就不会越界写）。Go 的 `var st unix.Stat_t` 是栈上结构体，C# 无 unsafe 只能用堆缓冲。
// 18) `newFanotifyBackend(noRename bool)` → `internal FanotifyBackend(bool noRename = false)`：noRename 只被
//     fanotify-no-rename 测试 watcher 用来强制走 FAN_MOVED_FROM/FAN_MOVED_TO 分支，这里同样保留。
// 19) Go 的 `init()` 是 `if fanotifyAvailable() { fanotifyWatcher.factory = … }`（**有条件**注册，探测会真的调一次
//     fanotify_init）→ C# 的 `[ModuleInitializer]` 同样先探测：`if (OperatingSystem.IsLinux() && FanotifyAvailable())`。
//     探测里的 DllNotFound/EntryPoint 之类异常被吞掉（探测失败 = 后端不可用），绝不让程序集加载失败。
// 20) Go 的 `readBuf []byte`（构造函数分配、随对象 GC）→ 构造函数里 `Marshal.AllocHGlobal(fanotifyBufferSize)`，
//     在 `CloseFDs`（poll 循环确定退出之后、放行 `_endedSignal` 之前）显式释放；只有 start 线程会读它。
// 21) Go 的 `int(meta.Event_len)` 里 int 是 64 位 → C# 用 `uint` 做边界比较（`eventLen < metadataLen ||
//     eventLen > remaining`），避免 `(int)uint` 在超过 2^31 时变负数的歧义；真实事件下两者等价。
// 22) Go 的 `open_by_handle_at` **没有被本后端用到**（x/sys 里有 `OpenByHandleAt`，fanotify_linux.go 只调
//     `NameToHandleAt`），按 1:1 原则不移植。
// 23) 线程模型与 Go 完全一致：一条长驻线程（`Start`，经 `WatcherBase.Run()` 启动）独占 poll(2) 循环与全部事件
//     分发；`Subscribe`/`CloseWatch` 在调用方线程、持 `_mu` 执行（因此 `Subscribe`/`CloseWatch`/`MarkDir` 内部
//     **不**加锁，由 `WatcherBase` 保证）。
// 24) 【只能静态对照】本机是 win32，本后端**无法运行验证**。以下只能逐条对照 Linux 头文件/x/sys 源码：
//     三张结构体偏移表（第 2、3 条）、全部 FAN_*/FANOTIFY_*/O_*/POLLIN 位值与 errno 常量、`Syscalls` 表里的
//     syscall 号与 statfs64 的 size 实参、以及 libc `syscall` 的固定原型调用约定。函数签名对照 glibc 的
//     sys/fanotify.h、fcntl.h(name_to_handle_at)、sys/vfs.h(statfs)、poll.h、unistd.h。
//
// ---------------------------------------------------------------------------
// fanotify_linux.go: Linux fanotify 后端
//
// 用 Linux 的 fanotify(7)（内核 ≥ 5.13 且无需 CAP_SYS_ADMIN）监听目录树。与 inotify 不同，fanotify 用基于 FID
// 的事件上报（FAN_REPORT_FID | FAN_REPORT_DFID_NAME）：每个事件带父目录的文件句柄与子项名，于是 watch 分发按
// (fsid, handle_type, handle_bytes) 而不是 wd 整数索引——彻底绕开 inotify 的 per-user watch 上限
// （fs.inotify.max_user_watches）。
//
//	┌──────────────────────────────────────────────────────────────┐
//	│                     fanotifyBackend                          │
//	│                                                              │
//	│  ┌───────────┐        poll(2)        ┌──────────────────┐    │
//	│  │ pipe[0]   ├──────────────────────►│  Start() 线程    │    │
//	│  │ (wakeup)  │                       │  (event loop)    │    │
//	│  └───────────┘                       └────────┬─────────┘    │
//	│  ┌───────────┐                                │              │
//	│  │ fanotify  ├──────────────────────►  HandleEvents()        │
//	│  │ fd        │                       ParseFanotifyDfidNames  │
//	│  └───────────┘                                │              │
//	│                                               ▼              │
//	│                               ┌─────────────────────────┐    │
//	│                               │ subscriptions           │    │
//	│                               │ map[handleKey] → []sub  │    │
//	│                               │  sub.dirWatch.events    │    │
//	│                               └─────────────────────────┘    │
//	│                                                              │
//	│  handleKey = (fsid, handle_type, handle_bytes)               │
//	│  经 statfs(2) + name_to_handle_at(2) 逐目录取得               │
//	└──────────────────────────────────────────────────────────────┘
//
// 关闭：向 pipe[1] 写一个字节 → poll 在 pipe[0] 上看到 POLLIN → 循环退出 → finally 关掉 fanotify fd、pipe fd，
// 并放行 endedSignal。
// ---------------------------------------------------------------------------
namespace TypeScript.FsWatch;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal sealed class FanotifyBackend : WatcherBase
{
    // ---- Go: const (…) --------------------------------------------------------

    /// <summary>Go: <c>fanotifyInitFlags</c>（= FAN_CLASS_NOTIF|FAN_CLOEXEC|FAN_NONBLOCK|FAN_REPORT_FID|FAN_REPORT_DFID_NAME = 0xC03）。</summary>
    private const uint _fanotifyInitFlags =
        _fanClassNotif | _fanCloexec | _fanNonblock | _fanReportFid | _fanReportDfidName;

    /// <summary>Go: <c>fanotifyMarkMaskBase</c>。</summary>
    private const ulong _fanotifyMarkMaskBase =
        _fanCreate | _fanDelete | _fanModify | _fanDeleteSelf | _fanMoveSelf | _fanOndir | _fanEventOnChild;

    /// <summary>Go: <c>fanotifyMarkMaskRename</c>（FAN_RENAME 可用时，Linux 5.17+）。</summary>
    private const ulong _fanotifyMarkMaskRename = _fanotifyMarkMaskBase | _fanRename;

    /// <summary>Go: <c>fanotifyMarkMaskMovedFromTo</c>（FAN_RENAME 不可用时的回退）。</summary>
    private const ulong _fanotifyMarkMaskMovedFromTo = _fanotifyMarkMaskBase | _fanMovedFrom | _fanMovedTo;

    /// <summary>Go: <c>fanotifyMarkAddFlags</c>（= FAN_MARK_ADD|FAN_MARK_ONLYDIR|FAN_MARK_DONT_FOLLOW = 0xD）。</summary>
    private const uint _fanotifyMarkAddFlags = _fanMarkAdd | _fanMarkOnlydir | _fanMarkDontFollow;

    /// <summary>Go: <c>fanotifyMarkRemoveFlags</c>（Go 里是内联的 `FAN_MARK_REMOVE|FAN_MARK_ONLYDIR` = 0xA）。</summary>
    private const uint _fanotifyMarkRemoveFlags = _fanMarkRemove | _fanMarkOnlydir;

    /// <summary>Go: <c>fanotifyBufferSize = 8192</c>。</summary>
    private const int _fanotifyBufferSize = 8192;

    // ---- 结构体偏移表（见文件头第 2、3 条）-------------------------------------

    /// <summary>Go: <c>unsafe.Sizeof(unix.FanotifyEventMetadata{})</c> = 24。</summary>
    private const int _eventMetadataSize = 24;
    private const int _eventMetadataEventLenOffset = 0;
    private const int _eventMetadataVersOffset = 4;
    private const int _eventMetadataMetadataLenOffset = 6;
    private const int _eventMetadataMaskOffset = 8;
    private const int _eventMetadataFdOffset = 16;

    /// <summary>Go: <c>parseFanotifyDfidNames</c> 里的 <c>infoHdrSize</c>（struct fanotify_event_info_header）。</summary>
    private const int _infoHeaderSize = 4;

    /// <summary>Go: <c>fsidSize</c>（__kernel_fsid_t = 2×s32）。</summary>
    private const int _fsidSize = 8;

    /// <summary>Go: <c>fhHdrSize</c>（struct file_handle 的头：handle_bytes + handle_type）。</summary>
    private const int _fileHandleHeaderSize = 8;
    private const int _fileHandleBytesOffset = 0;
    private const int _fileHandleTypeOffset = 4;

    /// <summary>`struct pollfd { int fd; short events; short revents; }`，长 8（各架构一致）。</summary>
    private const int _pollFdSize = 8;
    private const int _pollFdEventsOffset = 4;
    private const int _pollFdReventsOffset = 6;

    /// <summary>`struct statfs` 的缓冲大小：最大的 64 位布局是 120 字节，给 128 以免任何架构越界写。</summary>
    private const int _statfsBufferSize = 128;

    /// <summary>`struct stat` 的缓冲大小：x86-64 是 144 字节，给 256。只读返回值，一个字段都不解析。</summary>
    private const int _lstatBufferSize = 256;

    // ---- 位值与常量（x/sys/unix v0.48.0 zerrors_linux.go，逐条抄）---------------

    private const uint _fanCloexec = 0x0000_0001;
    private const uint _fanNonblock = 0x0000_0002;
    private const uint _fanClassNotif = 0x0000_0000;
    private const uint _fanReportFid = 0x0000_0200;
    private const uint _fanReportDfidName = 0x0000_0c00; // FAN_REPORT_DIR_FID | FAN_REPORT_NAME

    private const ulong _fanModify = 0x0000_0002;
    private const ulong _fanMovedFrom = 0x0000_0040;
    private const ulong _fanMovedTo = 0x0000_0080;
    private const ulong _fanCreate = 0x0000_0100;
    private const ulong _fanDelete = 0x0000_0200;
    private const ulong _fanDeleteSelf = 0x0000_0400;
    private const ulong _fanMoveSelf = 0x0000_0800;
    private const ulong _fanQOverflow = 0x0000_4000;
    private const ulong _fanEventOnChild = 0x0800_0000;
    private const ulong _fanRename = 0x1000_0000;
    private const ulong _fanOndir = 0x4000_0000;

    private const byte _fanEventInfoTypeDfidName = 0x2;
    private const byte _fanEventInfoTypeDfid = 0x3;
    private const byte _fanEventInfoTypeOldDfidName = 0xa;
    private const byte _fanEventInfoTypeNewDfidName = 0xc;

    private const byte _fanotifyMetadataVersion = 0x3;

    private const uint _fanMarkAdd = 0x0000_0001;
    private const uint _fanMarkRemove = 0x0000_0002;
    private const uint _fanMarkDontFollow = 0x0000_0004;
    private const uint _fanMarkOnlydir = 0x0000_0008;

    /// <summary>asm-generic/fcntl.h：O_RDONLY = 0（fanotify_init 的 event_f_flags）。</summary>
    private const uint _oRdonly = 0x0;

    private const uint _oCloexec = 0x0008_0000;
    private const uint _oNonblock = 0x0000_0800;

    private const short _pollIn = 0x001;

    /// <summary>asm-generic/fcntl.h：AT_FDCWD。</summary>
    private const int _atFdcwd = -100;

    // asm-generic/errno-base.h / errno.h（本文件用到的全部值）。EWOULDBLOCK == EAGAIN、
    // ENOTSUP == EOPNOTSUPP，故各自只保留一个常量（见文件头第 13 条）。
    private const int _eintr = 4;
    private const int _eagain = 11;
    private const int _enodev = 19;
    private const int _einval = 22;
    private const int _enosys = 38;
    private const int _eoverflow = 75;
    private const int _eopnotsupp = 95;

    // ---- Go: type fanotifyBackend struct 的字段 ---------------------------------

    /// <summary>Go: <c>pipeFDs [2]int</c>（-1 = 未创建或已关闭）。</summary>
    private readonly int[] _pipeFDs = [-1, -1];

    /// <summary>Go: <c>pipeWriteFD atomic.Int32</c>：只经 <c>Volatile</c> / <c>Interlocked</c> 访问。</summary>
    private int _pipeWriteFD = -1;

    /// <summary>Go: <c>fanotifyFD int</c>（-1 = 未创建或已关闭）。</summary>
    private int _fanotifyFD = -1;

    /// <summary>Go: <c>markMask uint64</c>：FAN_RENAME 或 FAN_MOVED_FROM|FAN_MOVED_TO；首次 subscribe 前为 0。</summary>
    private ulong _markMask;

    /// <summary>Go: <c>noRename bool</c>：跳过 FAN_RENAME 探测（仅供 no-rename 测试）。</summary>
    private readonly bool _noRename;

    /// <summary>Go: <c>subscriptions map[fanotifyHandleKey][]*fanotifySubscription</c>（multimap&lt;handleKey, sub&gt;）。</summary>
    private readonly Dictionary<FanotifyHandleKey, List<FanotifySubscription>> _subscriptions = [];

    /// <summary>Go: <c>endedSignal chan struct{}</c>（close = 就绪）。</summary>
    private readonly TaskCompletionSource _endedSignal = new();

    /// <summary>Go: <c>readBuf []byte</c>：跨 handleEvents 复用的读缓冲，只被 start 线程访问。</summary>
    private IntPtr _readBuf;

    /// <summary>Go: <c>watchersTouched map[*dirWatch]struct{}</c>：跨 handleEvents 复用，只被 start 线程访问。</summary>
    private readonly HashSet<DirWatch> _watchersTouched = [];

    /// <summary>
    /// Go: <c>func init() { if fanotifyAvailable() { fanotifyWatcher.factory = func() watcherImpl { return newFanotifyBackend(false) } } }</c>。
    /// 模块初始化器 = 「包被加载即注册」（见文件头第 19 条）：仍然按 Go 那样先探测 fanotify_init，只有可用才注册
    /// factory；`FsWatch.Fanotify()` 是 `FallbackWatcher(fanotify, inotify)`，探测失败时它会退回 inotify。
    /// </summary>
    [SuppressMessage("Usage", "CA2255", Justification = "Go 的 fanotify_linux.go init()：模块初始化器等价于「包被加载即注册」（见 FanotifyLinux.cs 文件头第 19 条）。")]
    [ModuleInitializer]
    internal static void InitializeFanotifyWatcher()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        bool available;
        try
        {
            available = FanotifyAvailable();
        }
        catch (Exception)
        {
            // 探测本身失败（例如 libc 里没有 syscall 符号）= 后端不可用；绝不让程序集加载失败（见文件头第 19 条）。
            available = false;
        }

        if (available)
        {
            FsWatch.FanotifyWatcherInstance.Factory = static () => new FanotifyBackend();
        }
    }

    /// <summary>Go: <c>func fanotifyAvailable() bool</c>。真的调一次 fanotify_init，成功则关掉 fd 并返回 true。</summary>
    internal static bool FanotifyAvailable()
    {
        if (OperatingSystem.IsAndroid())
        {
            return false;
        }

        if (Syscalls is null)
        {
            // 未知架构（Go 里是编译期就选好 SYS_* 号；C# 只能运行期判定，见文件头第 1 条）。
            return false;
        }

        if (!TryFanotifyInit(_fanotifyInitFlags, _oRdonly | _oCloexec, out var fd, out _))
        {
            return false;
        }

        _ = Close(fd);
        return true;
    }

    /// <summary>Go: <c>func newFanotifyBackend(noRename bool) *fanotifyBackend</c>（见文件头第 18、20 条）。</summary>
    internal FanotifyBackend(bool noRename = false)
    {
        _noRename = noRename;
        _readBuf = Marshal.AllocHGlobal(_fanotifyBufferSize);
        Init(this);
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) start() error</c>。由 <c>WatcherBase.Run()</c> 在后台线程调用。</summary>
    public override void Start()
    {
        // 建一条管道，好在 shutdown 时唤醒 poll(2) 循环。
        var fds = Marshal.AllocHGlobal(2 * sizeof(int));
        try
        {
            if (Pipe2(fds, (int)(_oCloexec | _oNonblock)) != 0)
            {
                throw Wrap("unable to open pipe", Marshal.GetLastPInvokeError());
            }

            _pipeFDs[0] = Marshal.ReadInt32(fds, 0);
            _pipeFDs[1] = Marshal.ReadInt32(fds, sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(fds);
        }

        Volatile.Write(ref _pipeWriteFD, _pipeFDs[1]); // Go: b.pipeWriteFD.Store(int32(b.pipeFDs[1]))

        // Go 的 `defer func() { b.closeFDs(); close(b.endedSignal) }()`（在 pipe2 成功之后注册）。
        try
        {
            if (!TryFanotifyInit(_fanotifyInitFlags, _oRdonly | _oCloexec, out var fd, out var initErrno))
            {
                throw Wrap("unable to initialize fanotify", initErrno);
            }

            _fanotifyFD = fd;

            var pollfds = Marshal.AllocHGlobal(2 * _pollFdSize);
            try
            {
                WritePollFd(pollfds, 0, _pipeFDs[0]);
                WritePollFd(pollfds, 1, _fanotifyFD);

                NotifyStarted();

                while (true)
                {
                    // 超时 500ms；EINTR 继续。内核每次调用都会重写 revents，故无需手动清零。
                    if (Poll(pollfds, 2, 500) < 0)
                    {
                        var errno = Marshal.GetLastPInvokeError();
                        if (errno == _eintr)
                        {
                            continue;
                        }

                        throw Wrap("unable to poll", errno);
                    }

                    if (ReadRevents(pollfds, 0) != 0)
                    {
                        break;
                    }

                    if (ReadRevents(pollfds, 1) != 0)
                    {
                        HandleEvents();
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pollfds);
            }
        }
        finally
        {
            CloseFDs();
            _endedSignal.TrySetResult();
        }
    }

    /// <summary>
    /// Go: <c>func (b *fanotifyBackend) closeFDs()</c>。poll 循环退出后在 start 线程上执行；取 <c>_mu</c> 是为了让
    /// 对 <c>_fanotifyFD</c>/<c>_pipeFDs</c> 的写入与 <c>closeWatch</c>/<c>subscribe</c> 里的读取同步。
    /// </summary>
    private void CloseFDs()
    {
        lock (_mu)
        {
            if (_pipeFDs[0] >= 0)
            {
                _ = Close(_pipeFDs[0]);
                _pipeFDs[0] = -1;
            }

            var fd = Interlocked.Exchange(ref _pipeWriteFD, -1);
            if (fd >= 0)
            {
                _ = Close(fd);
            }

            _pipeFDs[1] = -1;

            if (_fanotifyFD >= 0)
            {
                _ = Close(_fanotifyFD);
                _fanotifyFD = -1;
            }
        }

        // Go 的 readBuf 交给 GC；这里在事件循环确定退出后显式释放非托管缓冲（见文件头第 20 条）。
        // 之后不会再有人读它：HandleEvents 只在上面那个 poll 循环里被调用。
        if (_readBuf != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_readBuf);
            _readBuf = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Go: <c>func (b *fanotifyBackend) shutdown()</c>。往管道写一个字节并等 start 线程收尾；pipe write fd 用原子读，
    /// 以安全对抗 start 线程的 deferred closeFDs。
    /// </summary>
    public override void Shutdown()
    {
        var fd = Volatile.Read(ref _pipeWriteFD);
        if (fd < 0)
        {
            return;
        }

        var buf = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buf, (byte)'X'); // Go: unix.Write(int(fd), []byte{'X'})
            _ = Write(fd, buf, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        _endedSignal.Task.Wait();
    }

    /// <summary>
    /// Go: <c>func (b *fanotifyBackend) subscribe(w *dirWatch) error</c>。经 watcherBase 虚分派在 <c>_mu</c> 下调用
    /// （因此与 handleEvents 串行），失败抛 <see cref="DirWatchError"/>（Go 返回 *dirWatchError）。
    /// </summary>
    public override void Subscribe(DirWatch w)
    {
        // 首次 subscribe 时用实际 watch 目录探测 FAN_RENAME。FAN_RENAME（Linux 5.17+）让重命名产生一个成对事件；
        // 不支持时退回 FAN_MOVED_FROM/FAN_MOVED_TO（两个独立事件，语义等价）。内核用 EINVAL 拒绝未知 mask 位。
        if (_markMask == 0)
        {
            if (_noRename)
            {
                _markMask = _fanotifyMarkMaskMovedFromTo;
            }
            else
            {
                _markMask = _fanotifyMarkMaskRename;
                if (TryFanotifyMark(_fanotifyFD, _fanotifyMarkAddFlags, _fanotifyMarkMaskRename, _atFdcwd, w._physicalDir, out var markErrno))
                {
                    // B5: 给探测用的 Add 配一次 Remove。若 Remove 失败（现实中只有 EINTR 或内核资源紧张）就让它跟着
                    // 进程活到结束：下面 markDir 会用同样的 flag Add 真正的 mask，内核会把两者合并。只有这个失败
                    // 路径会被显式重试。
                    while (true)
                    {
                        if (TryFanotifyMark(_fanotifyFD, _fanotifyMarkRemoveFlags, _fanotifyMarkMaskRename, _atFdcwd, w._physicalDir, out var rmErrno))
                        {
                            break;
                        }

                        if (rmErrno != _eintr)
                        {
                            break;
                        }
                    }
                }
                else if (markErrno is _einval or _eopnotsupp)
                {
                    _markMask = _fanotifyMarkMaskMovedFromTo;
                }
            }
        }

        if (!w._recursive)
        {
            try
            {
                MarkDir(w, w._dir, w._physicalDir);
            }
            catch (Exception err)
            {
                throw new DirWatchError(new IOException($"fanotify_mark on '{w._dir}' failed: {err.Message}", err), w);
            }

            return;
        }

        try
        {
            FsWatch.WalkDir(w._physicalDir, true, (watchPath, isDir) =>
            {
                if (!isDir)
                {
                    return;
                }

                var path = w.DisplayPath(watchPath);
                try
                {
                    MarkDir(w, path, watchPath);
                }
                catch (Exception err)
                {
                    throw new DirWatchError(new IOException($"fanotify_mark on '{path}' failed: {err.Message}", err), w);
                }
            });
        }
        catch (Exception)
        {
            // Go: `_ = b.closeWatch(w); return err`（closeWatch 的错误被丢弃，见文件头第 16 条）。
            try
            {
                CloseWatch(w);
            }
            catch (Exception)
            {
            }

            throw;
        }
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) markDir(w *dirWatch, path string, markPath string) error</c>。</summary>
    private void MarkDir(DirWatch w, string path, string markPath)
    {
        if (!TryFanotifyMark(_fanotifyFD, _fanotifyMarkAddFlags, _markMask, _atFdcwd, markPath, out var markErrno))
        {
            throw MaybeWrapUnsupportedFilesystem(ErrnoErr(markErrno));
        }

        if (!TryNameToHandleAt(_atFdcwd, markPath, out var handleType, out var handleBytes, out var handleErrno))
        {
            // 拿不到句柄就没法跟踪这个目录：把它撤下来。
            _ = TryFanotifyMark(_fanotifyFD, _fanotifyMarkRemoveFlags, _markMask, _atFdcwd, markPath, out _);
            throw MaybeWrapUnsupportedFilesystem(Wrap("name_to_handle_at", handleErrno));
        }

        if (!TryStatfs(markPath, out var fsid0, out var fsid1, out var statfsErrno))
        {
            _ = TryFanotifyMark(_fanotifyFD, _fanotifyMarkRemoveFlags, _markMask, _atFdcwd, markPath, out _);
            throw Wrap("statfs", statfsErrno);
        }

        var key = FanotifyHandleKey.Make(fsid0, fsid1, handleType, handleBytes);
        var sub = new FanotifySubscription(path, markPath, w, key);
        if (!_subscriptions.TryGetValue(key, out var list))
        {
            list = [];
            _subscriptions[key] = list;
        }

        list.Add(sub); // Go: b.subscriptions[key] = append(b.subscriptions[key], sub)
    }

    /// <summary>
    /// Go: <c>func maybeWrapUnsupportedFilesystem(err error) error</c>。EOPNOTSUPP == ENOTSUP == 95，故单个常量即覆盖
    /// Go 的两个判定（见文件头第 14 条）。
    /// </summary>
    private static Exception MaybeWrapUnsupportedFilesystem(Exception err)
    {
        if (TryGetErrno(err, out var errno) && errno is _eopnotsupp or _enodev)
        {
            var unsupported = FsWatchException.FilesystemUnsupported();
            return new FsWatchException(
                FsWatchErrorKind.FilesystemUnsupported,
                $"{err.Message}: {unsupported.Message}",
                err);
        }

        return err;
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) handleEvents() error</c>。从 fd 读事件并分发。</summary>
    private void HandleEvents()
    {
        while (true)
        {
            var n = Read(_fanotifyFD, _readBuf, _fanotifyBufferSize);
            if (n < 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                if (errno == _eagain) // EWOULDBLOCK == EAGAIN == 11（见文件头第 13 条）
                {
                    break;
                }

                throw Wrap("Error reading from fanotify", errno);
            }

            if (n == 0)
            {
                break;
            }

            var offset = 0;
            var remaining = (int)n; // Go: data := buf[:n]

            while (remaining >= _eventMetadataSize)
            {
                var eventLen = (uint)Marshal.ReadInt32(_readBuf, offset + _eventMetadataEventLenOffset);
                var vers = Marshal.ReadByte(_readBuf, offset + _eventMetadataVersOffset);
                var metadataLen = (ushort)Marshal.ReadInt16(_readBuf, offset + _eventMetadataMetadataLenOffset);
                var mask = (ulong)Marshal.ReadInt64(_readBuf, offset + _eventMetadataMaskOffset);
                var metaFd = Marshal.ReadInt32(_readBuf, offset + _eventMetadataFdOffset);

                if (vers != _fanotifyMetadataVersion)
                {
                    throw new IOException($"unsupported fanotify metadata version: {vers}");
                }

                if (eventLen < metadataLen || eventLen > (uint)remaining)
                {
                    break;
                }

                // FID 模式下 fd 应该是 FAN_NOFD；万一有值也要关掉，免得泄 fd。
                if (metaFd >= 0)
                {
                    _ = Close(metaFd);
                }

                if ((mask & _fanQOverflow) != 0)
                {
                    HandleOverflow(_watchersTouched);
                    offset += (int)eventLen;
                    remaining -= (int)eventLen;
                    continue;
                }

                ParseFanotifyDfidNames(
                    _readBuf,
                    offset + metadataLen,
                    (int)eventLen - metadataLen,
                    out var primary,
                    out var renameTo);

                if ((mask & _fanRename) != 0)
                {
                    if (primary is not null || renameTo is not null)
                    {
                        HandleRenameEvent(mask, primary, renameTo, _watchersTouched);
                    }
                }
                else if (primary is not null)
                {
                    HandleParsedEvent(mask, primary, _watchersTouched);
                }

                offset += (int)eventLen;
                remaining -= (int)eventLen;
            }
        }

        foreach (var w in _watchersTouched)
        {
            w.Notify();
        }

        _watchersTouched.Clear(); // Go: clear(watchersTouched)
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) handleOverflow(touched map[*dirWatch]struct{})</c>。</summary>
    private void HandleOverflow(HashSet<DirWatch> touched)
    {
        lock (_mu)
        {
            var seen = new HashSet<DirWatch>();
            foreach (var subs in _subscriptions.Values)
            {
                foreach (var s in subs)
                {
                    if (!seen.Add(s._dirWatch))
                    {
                        continue;
                    }

                    s._dirWatch._events.SetError(FsWatchException.Overflow());
                    touched.Add(s._dirWatch);
                }
            }
        }
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) handleRenameEvent(…)</c>（FAN_RENAME 的成对事件）。</summary>
    private void HandleRenameEvent(ulong mask, FanotifyDfidName? dfidOld, FanotifyDfidName? dfidNew, HashSet<DirWatch> touched)
    {
        lock (_mu)
        {
            var isDir = (mask & _fanOndir) != 0;

            // 从旧位置摘掉。
            if (dfidOld is not null && dfidOld._name.Length != 0 && dfidOld._name != ".")
            {
                foreach (var s in SubsFor(dfidOld._key))
                {
                    var oldPath = s._path + "/" + dfidOld._name;

                    // 被重命名的是目录时，连同它所有后代的订阅一起摘掉。目的地在监听树之外时内核里的 mark 会泄漏：
                    // fanotify 没有与路径无关的 unmark，而我们并不为被标记目录持有 fd。
                    if (isDir)
                    {
                        DropSubsForPathAndDescendantsLocked(oldPath);
                    }

                    s._dirWatch._events.Remove(oldPath);
                    touched.Add(s._dirWatch);
                }
            }

            // 在新位置建出来。
            if (dfidNew is not null && dfidNew._name.Length != 0 && dfidNew._name != ".")
            {
                foreach (var s in SubsFor(dfidNew._key))
                {
                    var newPath = s._path + "/" + dfidNew._name;
                    s._dirWatch._events.Create(newPath);
                    if (isDir && s._dirWatch._recursive)
                    {
                        MarkNewDirectoryTree(s._dirWatch, newPath);
                    }

                    touched.Add(s._dirWatch);
                }
            }
        }
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) handleParsedEvent(…)</c>。</summary>
    private void HandleParsedEvent(ulong mask, FanotifyDfidName dfid, HashSet<DirWatch> touched)
    {
        lock (_mu)
        {
            // b.subscriptions[key] 里每个 *fanotifySubscription 指针最多出现一次（markDir 总是 append 新结构体），
            // 所以不需要去重。
            foreach (var s in SubsFor(dfid._key))
            {
                if (HandleSubscription(mask, dfid, s))
                {
                    touched.Add(s._dirWatch);
                }
            }
        }
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) handleSubscription(mask uint64, dfid *fanotifyDfidName, sub *fanotifySubscription) bool</c>。</summary>
    private bool HandleSubscription(ulong mask, FanotifyDfidName dfid, FanotifySubscription sub)
    {
        var w = sub._dirWatch;

        // 算出完整路径。自身事件（名字为空或 "."）直接用 watch 路径。
        var isSelfEvent = dfid._name.Length == 0 || dfid._name == ".";
        var path = isSelfEvent ? sub._path : sub._path + "/" + dfid._name;

        var isDir = (mask & _fanOndir) != 0;
        var touched = false;

        var hasDelete = (mask & (_fanDelete | _fanMovedFrom)) != 0;
        var hasCreate = (mask & (_fanCreate | _fanMovedTo)) != 0;

        // fanotify 会把同一对象上连续的多个事件合并成一个带多个 mask 位的事件。create 与 delete 位同时出现时，
        // 光看 mask 判断不出先后。stat 一下路径：还在 → 最后一步是 create（delete→create = "update"）；
        // 不在了 → 最后一步是 delete（create→delete = 相互抵消）。
        if (hasCreate && hasDelete && !isSelfEvent)
        {
            if (!PathExists(path))
            {
                // 创建后又被删掉：两个都记上，让它们抵消。
                w._events.Create(path);
                w._events.Remove(path);
                return true;
            }

            // 文件在：说明是删掉后又建出来的。继续走下面的"先删后建"流程，产出 "update"。
        }

        // 先处理 delete/move-from，这样合并的 DELETE+CREATE 会经由 eventList 的"快速重建"逻辑合成 "update"。
        if ((mask & (_fanDelete | _fanDeleteSelf | _fanMovedFrom | _fanMoveSelf)) != 0)
        {
            var isSelfMask = (mask & (_fanDeleteSelf | _fanMoveSelf)) != 0;

            // 除非是 watch 根，否则忽略自身的 delete/move 事件。
            if (!(isSelfMask && path != w._dir))
            {
                // 被删/被移走的是目录时，连它自己带全部后代一起摘订阅；否则那些（已经不在原地的）inode 之后报上来的
                // 事件会挂在过期的路径上。FAN_MOVED_FROM 把 inode 移出监听树时，内核对 inode 的 mark 只能泄漏：
                // fanotify 没有与路径无关的 unmark 手段，目的地在一切我们能解析的范围之外。
                // 自身事件可能不带 FAN_ONDIR（和 inotify 一样）。
                if (isSelfMask || isDir)
                {
                    DropSubsForPathAndDescendantsLocked(path);
                }
                else
                {
                    DropSubsForPathLocked(path);
                }

                w._events.Remove(path);
                touched = true;

                // watch 根被删：内核已经丢掉了 mark。除 delete 之外再抛出 ErrWatchTerminated，让调用方知道该收摊：
                // 这个 w 不会再有事件了。
                if (isSelfMask && path == w._dir)
                {
                    w._events.SetError(FsWatchException.WatchTerminated(
                        $"{FsWatchException.WatchTerminated().Message}: watched directory removed"));
                }
            }
        }

        if (hasCreate)
        {
            w._events.Create(path);
            if (isDir && w._recursive)
            {
                MarkNewDirectoryTree(w, path);
            }

            touched = true;
        }

        if ((mask & _fanModify) != 0)
        {
            w._events.Update(path);
            touched = true;
        }

        return touched;
    }

    /// <summary>
    /// Go: <c>handleSubscription</c>/<c>handleRenameEvent</c> 里"新目录出现（且递归）→ 整树 walk + mark"的那段
    /// （两处是同一段代码）：`_ = walkDir(w.physicalPath(path), true, …)` / `_ = b.markDir(…)`，错误全部丢弃。
    /// </summary>
    private void MarkNewDirectoryTree(DirWatch w, string path)
    {
        try
        {
            FsWatch.WalkDir(w.PhysicalPath(path), true, (p, pIsDir) =>
            {
                if (!pIsDir)
                {
                    return;
                }

                try
                {
                    MarkDir(w, w.DisplayPath(p), p);
                }
                catch (Exception)
                {
                    // Go: `_ = b.markDir(…)`（见文件头第 16 条）。
                }
            });
        }
        catch (Exception)
        {
            // Go: `_ = walkDir(…)`（见文件头第 16 条）。
        }
    }

    /// <summary>
    /// Go: <c>func (b *fanotifyBackend) parseFanotifyDfidNames(data []byte) (primary, rename *fanotifyDfidName)</c>。
    /// 从事件的信息记录区里取出 DFID_NAME 记录：主记录（DFID_NAME 或 OLD_DFID_NAME）与可选的第二条
    /// （NEW_DFID_NAME，仅 FAN_RENAME 事件）。C# 侧用 (ptr, offset, length) 表达 Go 的切片。
    /// </summary>
    private static void ParseFanotifyDfidNames(
        IntPtr data,
        int offset,
        int length,
        out FanotifyDfidName? primary,
        out FanotifyDfidName? rename)
    {
        primary = null;
        rename = null;

        for (var pos = 0; pos + _infoHeaderSize <= length;)
        {
            var infoType = Marshal.ReadByte(data, offset + pos);
            var infoLen = (int)(ushort)Marshal.ReadInt16(data, offset + pos + 2);
            if (infoLen < _infoHeaderSize || pos + infoLen > length)
            {
                break;
            }

            switch (infoType)
            {
                case _fanEventInfoTypeDfidName:
                case _fanEventInfoTypeOldDfidName:
                    var parsed = ParseFanotifyFidRecord(data, offset + pos, infoLen, true);
                    if (parsed is not null)
                    {
                        primary = parsed;
                    }

                    break;

                case _fanEventInfoTypeNewDfidName:
                    var parsedNew = ParseFanotifyFidRecord(data, offset + pos, infoLen, true);
                    if (parsedNew is not null)
                    {
                        rename = parsedNew;
                    }

                    break;

                case _fanEventInfoTypeDfid:
                    // 没有名字的 DFID：句柄标识的就是目录本身。还没找到 DFID_NAME 记录时作为回退。
                    if (primary is null)
                    {
                        var parsedDfid = ParseFanotifyFidRecord(data, offset + pos, infoLen, false);
                        if (parsedDfid is not null)
                        {
                            primary = parsedDfid;
                        }
                    }

                    break;
            }

            if (primary is not null && rename is not null)
            {
                return; // Go: return primary, rename
            }

            pos += infoLen;
        }
    }

    /// <summary>
    /// Go: <c>func parseFanotifyFidRecord(data []byte, hasName bool) *fanotifyDfidName</c>。解析单条
    /// <c>fanotify_event_info_fid</c> 记录（头 4 字节 + fsid 8 字节 + <c>struct file_handle</c>）。
    /// </summary>
    private static FanotifyDfidName? ParseFanotifyFidRecord(IntPtr data, int offset, int length, bool hasName)
    {
        const int minSize = _infoHeaderSize + _fsidSize + _fileHandleHeaderSize; // 20
        if (length < minSize)
        {
            return null;
        }

        var body = offset + _infoHeaderSize;
        var fsid0 = Marshal.ReadInt32(data, body);
        var fsid1 = Marshal.ReadInt32(data, body + 4);
        var handleBytes = (uint)Marshal.ReadInt32(data, body + 8);
        var handleType = Marshal.ReadInt32(data, body + 12);

        // Go: handleStart := fsidSize + fhHdrSize; if handleStart+handleBytes > len(body) { return nil }
        // （Go 的 int 是 64 位，这里用 long 比较，避免 uint 溢出后绕过边界检查。）
        var handleStart = _fsidSize + _fileHandleHeaderSize;
        var bodyLength = length - _infoHeaderSize;
        if (handleStart + (long)handleBytes > bodyLength)
        {
            return null;
        }

        var name = "";
        if (hasName)
        {
            var nameStart = handleStart + (int)handleBytes;
            if (nameStart < bodyLength)
            {
                name = ReadNulTerminatedUtf8(data, body + nameStart, bodyLength - nameStart);
            }
        }

        return new FanotifyDfidName
        {
            _key = FanotifyHandleKey.Make(fsid0, fsid1, handleType, ReadHandleKey(data, body + handleStart, (int)handleBytes)),
            _name = name,
        };
    }

    /// <summary>
    /// Go: <c>func (b *fanotifyBackend) dropSubsForPathLocked(path string)</c>。摘掉所有 <c>s.path == path</c> 的订阅，
    /// 不管它挂在哪个句柄键下。**必须在持有 <c>_mu</c> 时调用**。
    /// </summary>
    private void DropSubsForPathLocked(string path)
    {
        foreach (var key in new List<FanotifyHandleKey>(_subscriptions.Keys)) // 见文件头第 12 条
        {
            var list = _subscriptions[key];
            var kept = 0;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i]._path == path)
                {
                    continue;
                }

                list[kept++] = list[i]; // Go: kept := list[:0] + append（原地压缩，不额外分配）
            }

            CompactSubscriptions(key, list, kept);
        }
    }

    /// <summary>
    /// Go: <c>func (b *fanotifyBackend) dropSubsForPathAndDescendantsLocked(path string)</c>。摘掉 <c>s.path == path</c>
    /// 或严格位于其下的订阅。被移出监听树的 inode 上的内核 mark 仍然有效（fanotify 没有与路径无关的 unmark），
    /// 但把簿记摘掉可以避免之后的事件挂到已经无效的路径上。**必须在持有 <c>_mu</c> 时调用**。
    /// </summary>
    private void DropSubsForPathAndDescendantsLocked(string path)
    {
        foreach (var key in new List<FanotifyHandleKey>(_subscriptions.Keys)) // 见文件头第 12 条
        {
            var list = _subscriptions[key];
            var kept = 0;
            for (var i = 0; i < list.Count; i++)
            {
                if (IsPathOrDescendant(list[i]._path, path))
                {
                    continue;
                }

                list[kept++] = list[i];
            }

            CompactSubscriptions(key, list, kept);
        }
    }

    /// <summary>Go 在 drop* / closeWatch 里"原地压缩后若为空则删键、否则写回缩短的切片"的公共尾巴。</summary>
    private void CompactSubscriptions(FanotifyHandleKey key, List<FanotifySubscription> list, int kept)
    {
        if (kept == 0)
        {
            _subscriptions.Remove(key);
        }
        else if (kept < list.Count)
        {
            list.RemoveRange(kept, list.Count - kept); // 等价 Go 的 `b.subscriptions[key] = kept`
        }
    }

    /// <summary>Go: <c>func (b *fanotifyBackend) closeWatch(w *dirWatch) error</c>（恒返回 nil，C# 无返回值）。</summary>
    public override void CloseWatch(DirWatch w)
    {
        foreach (var key in new List<FanotifyHandleKey>(_subscriptions.Keys)) // 见文件头第 12 条
        {
            var list = _subscriptions[key];
            var kept = 0;
            var removedAny = false;
            var removedPath = "";
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (ReferenceEquals(s._dirWatch, w))
                {
                    removedAny = true;
                    removedPath = s._watchPath; // Go 里同列表内最后一个匹配者的 watchPath 生效
                    continue;
                }

                list[kept++] = s;
            }

            if (!removedAny)
            {
                continue;
            }

            if (kept == 0)
            {
                // 尝试 unmark。markMask 仍为 0 时（closeWatch 与"subscribe 还没来得及设 markMask 就 shutdown"竞争）
                // 整段跳过：mask=0 的 fanotify_mark 没有文档定义。ENOENT（目录可能已被删）与 EBADF（shutdown 时
                // fanotify fd 可能已关）都忽略。
                if (_markMask != 0)
                {
                    _ = TryFanotifyMark(_fanotifyFD, _fanMarkRemove, _markMask, _atFdcwd, removedPath, out _);
                }

                _subscriptions.Remove(key);
            }
            else
            {
                CompactSubscriptions(key, list, kept);
            }
        }
    }

    // ---- 订阅读取与路径判断 -----------------------------------------------------

    /// <summary>
    /// Go: <c>b.subscriptions[key]</c> 的遍历（见文件头第 11 条）。返回**快照数组**：C# 的 <c>foreach</c> 不允许在
    /// 迭代期间改 <c>List</c>，而这些循环体里会调用 drop*（原地压缩/删键）。Go 的 <c>range</c> 在原地压缩时可能
    /// 重复处理被搬到尾部的元素，C# 每个订阅只处理一次。
    /// </summary>
    private FanotifySubscription[] SubsFor(FanotifyHandleKey key) =>
        _subscriptions.TryGetValue(key, out var list) ? list.ToArray() : [];

    /// <summary>
    /// Go: `s.path == path || (len(s.path) > len(path) && s.path[len(path)] == '/' &amp;&amp; s.path[:len(path)] == path)`。
    /// **不复用** `FsWatch.IsInDirectoryOrSelf`：那条 helper 对以分隔符结尾的 dir（例如 "/"）会返回 true，而这里的
    /// 内联表达式不会——按 Go 的表达式逐字移植。
    /// </summary>
    private static bool IsPathOrDescendant(string candidate, string path)
    {
        if (candidate == path)
        {
            return true;
        }

        return candidate.Length > path.Length
            && candidate[path.Length] == '/'
            && candidate.StartsWith(path, StringComparison.Ordinal);
    }

    // ---- 非托管布局与 errno 辅助 -------------------------------------------------

    /// <summary>
    /// Go: 合并事件里的 `unix.Lstat(path, &amp;st) != nil`（只关心成功与否，见文件头第 17 条）。
    /// </summary>
    private static bool PathExists(string path)
    {
        var buf = Marshal.AllocHGlobal(_lstatBufferSize);
        var pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToCoTaskMemUTF8(path);
            return Lstat(pathPtr, buf) == 0;
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }

            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>把第 <paramref name="index"/> 个 pollfd 写成 { fd, POLLIN, revents = 0 }。</summary>
    private static void WritePollFd(IntPtr pollfds, int index, int fd)
    {
        var baseOffset = index * _pollFdSize;
        Marshal.WriteInt32(pollfds, baseOffset, fd);
        Marshal.WriteInt16(pollfds, baseOffset + _pollFdEventsOffset, _pollIn);
        Marshal.WriteInt16(pollfds, baseOffset + _pollFdReventsOffset, 0);
    }

    private static short ReadRevents(IntPtr pollfds, int index) =>
        Marshal.ReadInt16(pollfds, (index * _pollFdSize) + _pollFdReventsOffset);

    /// <summary>
    /// Go: <c>string(handleBytes)</c>——文件句柄的原始字节当作 map 键（Go 的字符串比较是逐字节的）。C# 逐字节映射成
    /// 一个字符（Latin-1）：这保证不同字节序列得到不同字符串。**不能**用 `Marshal.PtrToStringUTF8`——它会把非法
    /// UTF-8 序列折叠成 U+FFFD，使不同的句柄撞成同一个键。
    /// </summary>
    private static string ReadHandleKey(IntPtr data, int offset, int length)
    {
        if (length <= 0)
        {
            return "";
        }

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)Marshal.ReadByte(data, offset + i);
        }

        return new string(chars);
    }

    /// <summary>
    /// Go: 信息记录里 NUL 结尾的条目名 → <c>string(nameBytes)</c>（Go 保留原始字节，不做 UTF-8 校验）。C# 用 UTF-8
    /// 解码、非法序列替换成 U+FFFD（.NET 的路径字符串必须是合法 UTF-16；与 InotifyLinux.cs 的处理一致，见文件头第 6 条）。
    /// </summary>
    private static string ReadNulTerminatedUtf8(IntPtr data, int offset, int length)
    {
        var len = 0;
        while (len < length && Marshal.ReadByte(data, offset + len) != 0)
        {
            len++;
        }

        return len == 0 ? "" : Marshal.PtrToStringUTF8(IntPtr.Add(data, offset), len) ?? "";
    }

    /// <summary>Go 的裸 errno 错误（<c>errno.Errno</c>：只有 strerror 文本，没有前缀）。</summary>
    private static IOException ErrnoErr(int errno) => new(ErrnoText(errno), new Win32Exception(errno));

    /// <summary>Go 的 <c>fmt.Errorf("…: %w", err)</c>：消息拼接 + errno 放进 InnerException（见文件头第 13 条）。</summary>
    private static IOException Wrap(string message, int errno) =>
        new($"{message}: {ErrnoText(errno)}", new Win32Exception(errno));

    private static string ErrnoText(int errno) => new Win32Exception(errno).Message;

    /// <summary>沿 InnerException 链取 errno（= Go 的 <c>errors.Is(err, unix.EXxx)</c>）。</summary>
    private static bool TryGetErrno(Exception? err, out int errno)
    {
        for (var e = err; e is not null; e = e.InnerException)
        {
            if (e is Win32Exception w32)
            {
                errno = w32.NativeErrorCode;
                return true;
            }
        }

        errno = 0;
        return false;
    }

    // ---- 裸 syscall（x/sys 对这四个调用就是裸 syscall，见文件头第 1 条）-------------

    /// <summary>Go: <c>unix.FanotifyInit(flags uint, event_f_flags uint) (fd int, err error)</c>。</summary>
    private static bool TryFanotifyInit(uint flags, uint eventFlags, out int fd, out int errno)
    {
        fd = -1;
        errno = 0;

        var n = Syscalls;
        if (n is null)
        {
            errno = _enosys; // 未知架构：没有可用的 syscall 号
            return false;
        }

        var r = Syscall2(n.Value.FanotifyInit, (nint)flags, (nint)eventFlags);
        if (r < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            return false;
        }

        fd = (int)r;
        return true;
    }

    /// <summary>
    /// Go: <c>unix.FanotifyMark(fd int, flags uint, mask uint64, dirFd int, pathname string) error</c>。
    /// 32 位架构上 x/sys 把 64 位 mask 拆成 (low, high) 两个机器字，64 位架构上它占一个寄存器（见文件头第 1 条）。
    /// </summary>
    private static bool TryFanotifyMark(int fd, uint flags, ulong mask, int dirFd, string pathname, out int errno)
    {
        errno = 0;

        var n = Syscalls;
        if (n is null)
        {
            errno = _enosys;
            return false;
        }

        // Go 的 x/sys 在 pathname 为空串时传 NULL（本后端的调用点永远不会传空串，仅为 1:1 保留）。
        var pathPtr = pathname.Length == 0 ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(pathname);
        try
        {
            long r;
            if (IntPtr.Size == 8)
            {
                // zsyscall_linux_amd64.go: Syscall6(SYS_FANOTIFY_MARK, fd, flags, mask, dirFd, path, 0)
                r = Syscall5(n.Value.FanotifyMark, (nint)fd, (nint)flags, (nint)mask, (nint)dirFd, (nint)pathPtr);
            }
            else
            {
                // zsyscall_linux_386.go: Syscall6(SYS_FANOTIFY_MARK, fd, flags, mask, mask>>32, dirFd, path)
                r = Syscall6(
                    n.Value.FanotifyMark,
                    (nint)fd,
                    (nint)flags,
                    (nint)(uint)mask,
                    (nint)(uint)(mask >> 32),
                    (nint)dirFd,
                    (nint)pathPtr);
            }

            if (r < 0)
            {
                errno = Marshal.GetLastPInvokeError();
                return false;
            }

            return true;
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }
    }

    /// <summary>
    /// Go: <c>unix.NameToHandleAt(dirFD int, pathname string, flags int) (handle FileHandle, mountID int, err error)</c>。
    /// 照抄 x/sys/unix/syscall_linux.go 里的缓冲增长循环：先按 32 字节句柄试，EOVERFLOW 时按内核写回的
    /// <c>handle_bytes</c> 重来一次。返回的 mountID 在 fanotify 后端里被丢弃（Go 里是 `_`），但仍按 Go 传入实指针。
    /// </summary>
    private static bool TryNameToHandleAt(int dirFd, string path, out int handleType, out string handleBytes, out int errno)
    {
        handleType = 0;
        handleBytes = "";
        errno = 0;

        var n = Syscalls;
        if (n is null)
        {
            errno = _enosys;
            return false;
        }

        var size = 32 + _fileHandleHeaderSize;
        var didResize = false;
        while (true)
        {
            var buf = Marshal.AllocHGlobal(size);
            var mountId = Marshal.AllocHGlobal(sizeof(int));
            var pathPtr = IntPtr.Zero;
            try
            {
                // Go: fh.Bytes = size - unsafe.Sizeof(fileHandle{})（告诉内核 f_handle 的可用字节数）
                Marshal.WriteInt32(buf, _fileHandleBytesOffset, size - _fileHandleHeaderSize);
                pathPtr = Marshal.StringToCoTaskMemUTF8(path);

                var r = Syscall5(n.Value.NameToHandleAt, (nint)dirFd, (nint)pathPtr, (nint)buf, (nint)mountId, 0);
                if (r < 0)
                {
                    errno = Marshal.GetLastPInvokeError();
                    if (errno == _eoverflow)
                    {
                        if (didResize)
                        {
                            // Go: 不该需要第二次扩容。
                            return false;
                        }

                        didResize = true;
                        size = Marshal.ReadInt32(buf, _fileHandleBytesOffset) + _fileHandleHeaderSize;
                        continue;
                    }

                    return false;
                }

                handleType = Marshal.ReadInt32(buf, _fileHandleTypeOffset);
                handleBytes = ReadHandleKey(buf, _fileHandleHeaderSize, Marshal.ReadInt32(buf, _fileHandleBytesOffset));
                return true;
            }
            finally
            {
                if (pathPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }

                Marshal.FreeHGlobal(buf);
                Marshal.FreeHGlobal(mountId);
            }
        }
    }

    /// <summary>
    /// Go: <c>unix.Statfs(path string, buf *Statfs_t) error</c>——只取 <c>st.Fsid.Val</c> 的两个 int32。
    /// 32 位架构用 <c>statfs64(path, sizeof(struct statfs64), buf)</c>（x/sys 亦然），64 位用 <c>statfs(path, buf)</c>。
    /// </summary>
    private static bool TryStatfs(string path, out int fsid0, out int fsid1, out int errno)
    {
        fsid0 = 0;
        fsid1 = 0;
        errno = 0;

        var n = Syscalls;
        if (n is null)
        {
            errno = _enosys;
            return false;
        }

        var buf = Marshal.AllocHGlobal(_statfsBufferSize);
        var pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToCoTaskMemUTF8(path);
            var r = n.Value.StatfsTakesSize
                ? Syscall3(n.Value.Statfs, (nint)pathPtr, n.Value.StatfsSize, (nint)buf)
                : Syscall2(n.Value.Statfs, (nint)pathPtr, (nint)buf);
            if (r < 0)
            {
                errno = Marshal.GetLastPInvokeError();
                return false;
            }

            fsid0 = Marshal.ReadInt32(buf, n.Value.StatfsFsidOffset);
            fsid1 = Marshal.ReadInt32(buf, n.Value.StatfsFsidOffset + 4);
            return true;
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }

            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>当前进程架构的 syscall 号表；未知架构 / 非 Linux 返回 null（该架构上后端不可用）。</summary>
    private static LinuxSyscalls? Syscalls => _syscalls;

    private static readonly LinuxSyscalls? _syscalls = ResolveSyscalls();

    private static LinuxSyscalls? ResolveSyscalls()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        // syscall 号逐条抄自 x/sys/unix/zsysnum_linux_<goarch>.go（go.mod 锁定的 v0.48.0）：
        //   amd64:    FANOTIFY_INIT=300 FANOTIFY_MARK=301 NAME_TO_HANDLE_AT=303 STATFS=137
        //   arm64/riscv64/loong64（asm-generic）: 262 / 263 / 264 / 43
        //   ppc64le:  323 / 324 / 345 / 99          s390x: 332 / 333 / 335 / 99
        //   386:      338 / 339 / 341 / STATFS64=268（x/sys 的 Statfs 在 386 上走 statfs64）
        //   arm:      367 / 368 / 370 / STATFS64=266
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => new LinuxSyscalls(300, 301, 303, 137, false, 0, 56),
            Architecture.Arm64 => new LinuxSyscalls(262, 263, 264, 43, false, 0, 56),
            Architecture.RiscV64 => new LinuxSyscalls(262, 263, 264, 43, false, 0, 56),
            Architecture.LoongArch64 => new LinuxSyscalls(262, 263, 264, 43, false, 0, 56),
            Architecture.Ppc64le => new LinuxSyscalls(323, 324, 345, 99, false, 0, 56),
            Architecture.S390x => new LinuxSyscalls(332, 333, 335, 99, false, 0, 48),
            Architecture.X86 => new LinuxSyscalls(338, 339, 341, 268, true, 84, 48),
            Architecture.Arm or Architecture.Armv6 => new LinuxSyscalls(367, 368, 370, 266, true, 88, 48),
            _ => null,
        };
    }

    /// <summary>
    /// C# 侧新增（Go 里没有对应类型）：当前架构的 syscall 号 + statfs 变体/布局。
    /// <c>StatfsTakesSize</c> 为真时用 <c>statfs64(path, StatfsSize, buf)</c>（32 位架构），否则 <c>statfs(path, buf)</c>；
    /// <c>StatfsFsidOffset</c> 是 <c>__kernel_fsid_t</c>（两个 int32）在缓冲里的偏移（见文件头第 3 条）。
    /// </summary>
    private readonly record struct LinuxSyscalls(
        int FanotifyInit,
        int FanotifyMark,
        int NameToHandleAt,
        int Statfs,
        bool StatfsTakesSize,
        int StatfsSize,
        int StatfsFsidOffset);

    // ---- libc（Linux glibc/musl；除 syscall 外全部 SetLastError = true）-----------

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Syscall2(long number, nint a1, nint a2);

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Syscall3(long number, nint a1, nint a2, nint a3);

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Syscall5(long number, nint a1, nint a2, nint a3, nint a4, nint a5);

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Syscall6(long number, nint a1, nint a2, nint a3, nint a4, nint a5, nint a6);

    [DllImport("libc", EntryPoint = "pipe2", SetLastError = true)]
    private static extern int Pipe2(IntPtr pipefd, int flags);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int Poll(IntPtr fds, nuint nfds, int timeout);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint Read(int fd, IntPtr buf, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint Write(int fd, IntPtr buf, nuint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    /// <summary>Go: <c>unix.Lstat(path, &amp;st)</c>（本文件只用返回值，见文件头第 17 条）。</summary>
    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int Lstat(IntPtr pathname, IntPtr statbuf);
}

/// <summary>Go: <c>type fanotifySubscription struct</c>。一次订阅（同一个句柄键下可能有多个订阅）。</summary>
internal sealed class FanotifySubscription(string path, string watchPath, DirWatch dirWatch, FanotifyHandleKey key)
{
    /// <summary>Go: <c>fanotifySubscription.path</c>。调用方可见路径（被标记目录的展示路径）。</summary>
    internal string _path = path;

    /// <summary>Go: <c>fanotifySubscription.watchPath</c>。实际被标记的路径（物理路径）。</summary>
    internal string _watchPath = watchPath;

    /// <summary>Go: <c>fanotifySubscription.dirWatch</c>。</summary>
    internal DirWatch _dirWatch = dirWatch;

    /// <summary>Go: <c>fanotifySubscription.key</c>（与字典键重复；Go 里只写不读，为结构 1:1 保留）。</summary>
    internal FanotifyHandleKey _key = key;
}

/// <summary>Go: <c>type fanotifyDfidName struct</c>。从信息记录里解析出的目录 FID + 子项名。</summary>
internal sealed class FanotifyDfidName
{
    /// <summary>Go: <c>fanotifyDfidName.key</c>（父目录的句柄键）。</summary>
    internal FanotifyHandleKey _key;

    /// <summary>Go: <c>fanotifyDfidName.name</c>。子项名；目录自身的自事件为 ""。</summary>
    internal string _name = "";
}

/// <summary>
/// Go: <c>type fanotifyHandleKey struct</c>。等值比较的复合键：fsid（两个 int32）+ 句柄类型 + 句柄字节
/// （Go 用 <c>string(handleBytes)</c> 拿到可比性，C# 用 record struct 的结构化相等）。
/// </summary>
internal readonly record struct FanotifyHandleKey(int Fsid0, int Fsid1, int HandleType, string Handle)
{
    /// <summary>Go: <c>func makeFanotifyHandleKey(fsid [2]int32, handleType int32, handleBytes []byte) fanotifyHandleKey</c>。</summary>
    internal static FanotifyHandleKey Make(int fsid0, int fsid1, int handleType, string handle) =>
        new(fsid0, fsid1, handleType, handle);
}
