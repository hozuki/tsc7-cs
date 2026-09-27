// ported from: internal/fswatch/kqueue.go:1-785
//
// C# 映射与偏离（逐条）：
// 1) Go 用 `golang.org/x/sys/unix` 的封装（Kqueue/Pipe/Kevent/Open/Close/Write/Lstat/Fstat）→ C# 直接
//    `DllImport("libc")` 同名符号（全部 `SetLastError = true`，errno 取 `Marshal.GetLastPInvokeError()`）。
//    **不使用 `unsafe`**（本项目未开 AllowUnsafeBlocks）：struct kevent 与 struct stat 都在
//    `Marshal.AllocHGlobal` 出来的非托管缓冲上按字段偏移读写（`Marshal.ReadInt64/ReadInt32/ReadInt16`、
//    `Marshal.WriteInt64/WriteInt32/WriteInt16`）。
// 2) Go 的 `[]unix.Kevent_t{ev}`（每次注册现造一个切片，逃逸到堆）→ C# 复用**一个** `_changeBuf`（单元素）。
//    安全性依据：所有带 changelist 的 kevent 调用都持 `_kqMu`（subscribe / watchPath / tryRewatchLocked，
//    后两者由前者或事件循环在锁内调用），唯一无锁的一次是事件循环启动**之前**的管道注册；而事件循环里那次
//    **阻塞**的 kevent 既不带 changelist 也不持锁。
// 3) **struct kevent 布局表**（x86-64 / arm64；来源：各自 sys/event.h 与 x/sys 的 ztypes_*.go，逐条核对）：
//      darwin / openbsd / dragonfly：ident uintptr +0、filter int16 +8、flags uint16 +10、fflags uint32 +12、
//            data intptr +16、udata void* +24 → **32 字节**（x/sys Kevent_t 同形）。
//      freebsd：字段偏移同上，但 FreeBSD 12+ 在末尾多了 `ext[4]`（uint64）→ **64 字节**
//            （x/sys Kevent_t 末尾有 `Ext [4]uint64`；`kevent_fetch_changes` 用 `sizeof(struct kevent)`
//             逐个 copyin/copyout，故数组 stride 必须按 64 走）。
//      netbsd（netbsd-9 与 netbsd-10 头文件一致）：ident uintptr +0、filter **uint32** +8、flags **uint32**
//            +12、fflags **uint32** +16、pad +20、data int64 +24、udata +32 → **40 字节**
//            （x/sys Kevent_t 也是 Filter/Flags/Fflags 为 uint32 + `Pad_cgo_0 [4]byte`）。
//      netbsd-current（11+）：头文件给 struct kevent 加了 `ext[4]` 并把原型 __RENAME 到 `__kevent100`
//            （72 字节）——**本移植不支持**（见第 4 条）。
//    因此 stride 与字段偏移按 OS 取值（`_keventSize` / `_keventFlagsOffset` / `_keventFflagsOffset`），
//    而不是照抄固定 32。**只能静态对照**（本机 win32）。
// 4) NetBSD 的 `kevent` 原型在 netbsd-9/netbsd-10 的 sys/event.h 里是 `__RENAME(__kevent50)`；C# 按名字
//    P/Invoke 时能否解析取决于 libc 是否同时导出未版本化的弱别名（Go 不受影响：x/sys 走**裸系统调用**，
//    从不绑定 libc 符号名）。→ 先按 `kevent` 调用，一旦 `EntryPointNotFoundException` 就永久切到
//    `__kevent50`。两者在 netbsd-9/10 上是**同一个 40 字节 struct kevent 的 ABI**，故与本文件的布局选择自洽；
//    NetBSD 的 `stat`/`lstat`/`fstat`（`__stat50` 系列）不同，见第 9 条。
// 5) Go 的 `timeout *timespec` 参数**恒为 nil**（`unix.Kevent(kq, nil, events, nil)` 与
//    `unix.Kevent(kq, []Kevent_t{ev}, nil, nil)`：阻塞等待、没有超时）→ C# 传 `IntPtr.Zero`，**不定义**
//    timespec 结构（避免造出永远不会被使用的布局）。备忘：darwin/BSD 的
//    `struct timespec { time_t tv_sec(long, +0); long tv_nsec(+8); }` = 16 字节、8 字节对齐。
// 6) Go 的 `entry.state any`（nil = 未打开；int = 已打开的 fd）→ C# `_fd`（**-1 = 未打开**），避免装箱；
//    逐处对应 Go 的 `state == nil` / `state.(int)`。
// 7) Go 的 `openForEvents(path) (int, error)`：三个调用点都把失败当**正常分支**（skip / return false）→
//    C# `TryOpenForEvents`（契约 §3.2 的 `(T, bool)` → `TryX`）。失败后调用方**立即**读
//    `Marshal.GetLastPInvokeError()`：该 helper 内部只做一次 open，中间没有别的 P/Invoke，errno 未被覆盖。
// 8) Go 在 `tryRewatchLocked` 里用 `unix.Lstat` + `st.Mode & S_IFMT == S_IFDIR` 判断「重建后的类型是否与
//    原来一致」→ C# 用 `FsWatch.IsDirectoryNoFollow`（.NET 在 Unix 上就是 lstat 语义，且把符号链接
//    （ReparsePoint）视为非目录，正好等于 Go 的 `S_IFMT` 判定）。唯一差异：「路径已不存在且原条目是文件」
//    时会多一次必然失败的 open，返回值一致（false）；这是为避开未经验证的 struct stat 偏移。
// 9) Go 在 `compareDir` 里用 `unix.Fstat(fd)` / `unix.Lstat(path)` 比较 `st_dev`/`st_ino`（应对
//    DragonFlyBSD rename-over 会留下指向旧 inode 的僵尸 fd）→ C# 用 libc `fstat`/`lstat`，只读两个字段：
//      freebsd：st_dev uint64 +0、st_ino uint64 +8（x/sys ztypes_freebsd_amd64.go 头两个字段，已核对）。
//      darwin ：st_dev int32 +0、st_ino uint64 +8（x/sys ztypes_darwin_amd64.go 头两个字段，已核对）。
//    **只在 FreeBSD 上启用**（`_supportsIdentityCheck`）：
//      - NetBSD 的 `stat`/`lstat`/`fstat` 在头文件里被 `__RENAME(__stat50/__lstat50/__fstat50)`，按名字 P/Invoke
//        能否解析未经验证（且这段逻辑只服务于 DragonFlyBSD 的 rename-over 异常，跳过它只是少一次"同路径重建"探测）。
//      - macOS 上关闭的原因见 `_supportsIdentityCheck` 的文档注释：偏移表是 INODE64 布局，而裸 `fstat`/`lstat`
//        符号是遗留的 INODE32 ABI（头文件把 `fstat` 宏成 `fstat$INODE64`），会读到与偏移表不符的 dev/ino。
//        该检查在 macOS 上恒为 no-op（Go 注释指明异常只出现在 DragonFlyBSD），关闭它不改变可观察行为。
//    两个 512 字节 stat 缓冲**首次使用时分配一次**（Go 是栈上 `unix.Stat_t`；C# 无 unsafe 取不到栈地址），
//    只被事件循环线程使用（与 Go 的 `watchersTouched` 同一个线程约束）。
// 10) `readEntries`（Go: `os.ReadDir`）→ `Directory.GetFileSystemEntries` + `FsWatch.IsDirectoryNoFollow`
//     （与已落地的 WalkDir.cs 同样的取舍：不复制那套平台相关的 dirent 偏移；代价是每个条目多一次 lstat，
//     Go 只在 `d_type == DT_UNKNOWN` 时才回退 lstat）。名字按 ordinal 排序，与 Go 的 os.ReadDir 一致；
//     "." / ".." 同样不出现（GetFileSystemEntries 不返回）。目录不可读/不是目录 → 返回 false（Go 的 err != nil）。
// 11) Go 的 `walkDir` 回调返回 error 即中止、调用点 `_ = walkDir(...)` 丢弃错误 → C# `try { FsWatch.WalkDir(...) }
//     catch (IOException) { }`：只吞 walkDir 唯一会抛的「环境」异常，不掩盖编程错误（对照注释逐处标注）。
// 12) Go 允许在 `range map` 期间 `delete`（`subscribe` 的建 fd 循环、`removeEntryAndDescendants*`、
//     `compareDir` 的删除集合、`closeWatch`/`closeSubscriptions` 的遍历）→ C# 的 Dictionary/List 不允许 →
//     先对**键**做快照、或在确认要删的键后统一删。语义等价：这些循环的删除判定只看 key 内容，且被删的键
//     都在本次遍历中已被处理（或将被统一处理），没有「删除尚未访问的键」这种顺序依赖。
// 13) Go 用 `kept := list[:0]` 原地压缩切片 → C# `List.RemoveAll`（同样原地压缩、保持相对顺序）。
//     `closeWatch` 里「kept 为空就关 `list[0].fd`」→ 先取 `list[0]._fd` 再 RemoveAll（等价：Go 在 kept
//     为空时 `list[0]` 从未被就地覆盖，读到的就是原第一个元素）。
// 14) Go 的 `map[*dirWatch]struct{}` → `HashSet<DirWatch>`（引用相等）；`_watchersTouched` 是**跨 pass 复用**
//     的那个集合（Go: `b.watchersTouched`，注释明确只由 start goroutine 访问），每个 pass 结尾 `clear()` →
//     `HashSet.Clear()`：同一个实例，handleFileEvent/compareDir 收到的就是它。
// 15) Go 的 `chan struct{} endedSignal`（close = 就绪）→ `TaskCompletionSource`；`close(...)` → `TrySetResult()`
//     （幂等，且 Go 的二次 close 会 panic 而这里不会）。`shutdown()` 里 `pipeWriteFD.Load() < 0` 直接返回 ⇒
//     Start 之前、或 kqueue 创建失败之后调用 Shutdown 都不会卡住（两条路径都与 Go 一致）。
// 16) Go 的 `pipeWriteFD atomic.Int32` → `Volatile.Read` / `Interlocked.Exchange`（= Go 的 Load / Swap）；
//     字段初值 -1 承担 Go 构造函数里的 `Store(-1)`。
// 17) 三处**逐字相同**的 EVFILT_VNODE 注册块（tryRewatchLocked / subscribe / watchPath）→ 抽成
//     `TryRegisterVnode(fd)`（等价重构；Go 复制了三份）。`handleFileEvent` 里内联的那段关 fd（Go 那里没调
//     `closeEntryLocked`）保持内联逐行对照。该 helper 里另有一处**必需的**守卫：Go 在 Start 之前/收尾之后
//     （`kq == -1`）调用 `unix.Kevent(-1, …)` 只会拿到 EBADF；C# 若照着调用就要先解引用 `_changeBuf`
//     （`IntPtr.Zero` → AccessViolation 致命），故在 `_kq < 0 || _changeBuf == IntPtr.Zero` 时
//     `Marshal.SetLastPInvokeError(EBADF)` 并返回 false —— 与 Go 的可观察结果（同一条错误消息）完全一致。
// 18) Go 的文件头注释说 `kqueueBackend` 嵌入 `treeReaderBackend`，但**该类型在整个 fswatch 包里并不存在**
//     （全仓 grep 只命中那一句注释，结构体实际只嵌入 `watcherBase`）→ 没有可内联的实现，按注释之外的代码
//     逐行移植；原注释里的框图/时序说明以 `///` 文档注释保留。
// 19) 常量只保留 Go 源码真正用到的位（逐个对头文件核对）：
//      EVFILT_READ  = -1（netbsd 为 0）        EV_ADD    = 0x0001
//      EVFILT_VNODE = -4（netbsd 为 **3**）    EV_ENABLE = 0x0004
//      EV_CLEAR     = 0x0020                   EV_ERROR  = 0x4000
//      NOTE_DELETE  = 0x0001  NOTE_WRITE = 0x0002  NOTE_EXTEND = 0x0004
//      NOTE_ATTRIB  = 0x0008  NOTE_RENAME = 0x0020 NOTE_REVOKE = 0x0040
//    Go 没用到的 EV_DELETE(0x0002) / EV_ONESHOT(0x0010) / EV_RECEIPT(0x0040) / NOTE_LINK(0x0010) 与
//    O_CLOEXEC(darwin 0x01000000) 不定义（不留死常量）。`openForEvents` 也**不加** O_CLOEXEC：Go 走
//    `unix.Open(path, flags, 0)`，没有 CLOEXEC。
// 20) Go 的裸 errno 错误 → C# `IOException`，消息与 Go 的 `fmt.Errorf("…: %w", err)` 拼接结果一致，
//    errno 放进 InnerException（`Win32Exception.NativeErrorCode`），与 InotifyLinux.cs 同一约定。
//    `err == unix.EINTR` → `Marshal.GetLastPInvokeError() == 4`（EINTR 在 darwin/FreeBSD/NetBSD 上都是 4）。
// 21) `b.mu`（kqueueBackend 自己的锁，与嵌入的 watcherBase.mu **不是**同一个）→ C# `_kqMu`：基类已经占用了
//    `_mu` 这个名字，子类里不能再叫 `_mu`（否则锁到基类的互斥量）。锁序不变：watcherBase.mu → b.mu。
// 22) `closeFDs` 里加了 `lock (_kqMu)`：Go 在该函数里不取锁（`b.kq` 与订阅线程的读取构成良性竞争），
//    C# 侧照 InotifyLinux.cs 的同一处理把它纳入锁内，保证 `_kq`/管道 fd 的写入与 subscribe/closeWatch
//    的读取之间没有撕裂读。**不改变**任何可观察语义（该锁是叶子锁，持锁期间不做阻塞调用）。
// 23) Go 的 `func init()`（`kqueueWatcher.factory = …`）→ `[ModuleInitializer]`（parser/jsdoc.go 已有先例），
//    守卫是 `IsMacOS() || IsFreeBSD() || _isNetBsd`（C# 没有构建标签）。**偏离任务书的一处**：任务书写的是
//    `OperatingSystem.IsNetBSD()`，但 net11.0 的 `OperatingSystem` 里没有这个成员（仓内 Watcher.cs 文件头第 10 条
//    早就记录了「.NET 的 Is* 只有 IsFreeBSD，没有 OpenBSD/NetBSD/DragonFly」），故 NetBSD 改用
//    `RuntimeInformation.IsOSPlatform(OSPlatform.Create("NETBSD"))`（.NET 对未内置平台的标准写法）。Go 的
//    build tag 还包含 openbsd/dragonfly：本移植的注册面按任务书只覆盖 macOS/FreeBSD/NetBSD，但常量/偏移表对
//    openbsd/dragonfly 同样成立（它们与 darwin 同构）。
// 24) 本机是 win32，本后端**无法运行验证**：第 3、9 条的布局表、O_EVTONLY/O_RDONLY、EV_*/NOTE_* 位值、
//    EINTR、kevent 的 libc 签名（darwin/freebsd 是 `int nchanges`、netbsd 是 `size_t nchanges`——传 int 正数
//    后高位为零、等价）、第 4 条的 NetBSD 符号名回退，**只能静态对照**（见各条引用的头文件/ztypes 源）。
// 25) Go 的 `string(filepath.Separator)` → `Path.DirectorySeparatorChar`（darwin/BSD 上就是 '/'，与 Go 的
//    runtime 值相同）；`len(k) > len(path) && k[len(path)] == Separator && k[:len(path)] == path` 这类判定用
//    ordinal `StartsWith` + 分隔符字符比较实现（'/' 是 ASCII、不会落在代理对中间，与 Go 的字节下标判定等价）。
//    Go 的 `events := make([]unix.Kevent_t, 128)`（堆）、每次注册现造的 changelist 切片（逃逸到堆）、以及
//    compareDir 里栈上的两个 `unix.Stat_t` → C# 在 Start 里各分配一次、在 start 的 finally（CloseFDs）里释放
//    （与 InotifyLinux.cs 对 readBuf 的同一处理）；stat 缓冲改成惰性分配，因为它们只被事件循环线程用到。
namespace TypeScript.FsWatch;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Go: <c>type kqueueBackend struct</c>。kqueue/EVFILT_VNODE 后端（macOS、FreeBSD、NetBSD）。
///
///	┌───────────┐       kevent(2)        ┌──────────────────┐
///	│ pipe[0]   ├───────────────────────►│                  │
///	│ (wakeup)  │                        │  Start() 线程    │
///	└───────────┘                        │  (event loop)    │
///	┌───────────┐      EVFILT_VNODE      │                  │
///	│ kqueue fd ├───────────────────────►│                  │
///	└───────────┘                        └────────┬─────────┘
///	                                              ▼
///	            ┌──────────────────────────────────────────────┐
///	            │  fdToEntry:  map[fd]   → dirEntry            │
///	            │  subsByPath: map[path] → []kqueueSubscription │
///	            │  Each dirEntry._fd stores the open fd        │
///	            └──────────────────────────────────────────────┘
///
/// 线程：一条长驻线程（<c>Start</c>，经 <c>WatcherBase.Run()</c> 启动）独占 kevent(2) 循环，全部事件分发
/// （<c>CompareDir</c>/<c>HandleFileEvent</c>）都在它上面执行，访问 fd/订阅表时取 <c>_kqMu</c>；
/// <c>Subscribe</c>/<c>CloseWatch</c> 在调用方线程、持 watcherBase 的 <c>_mu</c> 执行（<c>WatchPath</c>
/// 再取 <c>_kqMu</c>）。回调投递仍然走进程级 debouncer（50ms/500ms 窗口），不在调用方线程、也不在事件循环
/// 线程上执行。
///
/// 关闭：向 pipe[1] 写一个字节 → kevent 在 pipe[0] 上返回 → 循环退出 → finally 里关掉全部 fd、kqueue 与管道，
/// 并放行 <c>Shutdown</c> 的等待者。
/// </summary>
internal sealed class KqueueBackend : WatcherBase
{
    /// <summary>Go: <c>events := make([]unix.Kevent_t, 128)</c>。</summary>
    private const int _maxEvents = 128;

    // ---- struct kevent 偏移/尺寸（见文件头第 3 条）--------------------------------

    /// <summary>三个平台的 <c>ident</c> 都在 +0（uintptr，64 位）。</summary>
    private const int _keventIdentOffset = 0;

    /// <summary>三个平台的 <c>filter</c> 都在 +8（darwin/freebsd 是 int16，netbsd 是 uint32）。</summary>
    private const int _keventFilterOffset = 8;

    /// <summary>darwin/openbsd/dragonfly（末尾无 ext[]）。</summary>
    private const int _keventSizeDarwin = 32;

    /// <summary>FreeBSD 12+（末尾 ext[4] uint64）。</summary>
    private const int _keventSizeFreeBsd = 64;

    /// <summary>netbsd-9/netbsd-10（filter/flags/fflags 为 uint32 + 4 字节 pad，末尾无 ext[]）。</summary>
    private const int _keventSizeNetBsd = 40;

    private const int _keventFlagsOffsetDarwin = 10;
    private const int _keventFflagsOffsetDarwin = 12;
    private const int _keventFlagsOffsetNetBsd = 12;
    private const int _keventFflagsOffsetNetBsd = 16;

    // ---- sys/event.h 的位（见文件头第 19 条）--------------------------------------

    private const uint _evAdd = 0x0001;
    private const uint _evEnable = 0x0004;
    private const uint _evClear = 0x0020;
    private const uint _evError = 0x4000;

    private const uint _noteDelete = 0x0001;
    private const uint _noteWrite = 0x0002;
    private const uint _noteExtend = 0x0004;
    private const uint _noteAttrib = 0x0008;
    private const uint _noteRename = 0x0020;
    private const uint _noteRevoke = 0x0040;

    /// <summary>Go: <c>ev.Fflags = NOTE_DELETE|NOTE_WRITE|NOTE_EXTEND|NOTE_ATTRIB|NOTE_RENAME|NOTE_REVOKE</c>。</summary>
    private const uint _vnodeNotes =
        _noteDelete | _noteWrite | _noteExtend | _noteAttrib | _noteRename | _noteRevoke;

    // ---- 标志位与 errno ----------------------------------------------------------

    /// <summary>fcntl.h：O_RDONLY = 0（darwin 之外的 BSD 用）。</summary>
    private const int _oRdOnly = 0;

    /// <summary>darwin 的 fcntl.h：O_EVTONLY = 0x00008000（只申请事件通知、不要读权限）。</summary>
    private const int _oEvtOnly = 0x8000;

    /// <summary>errno.h：EINTR = 4（darwin / FreeBSD / NetBSD 同值）。</summary>
    private const int _eintr = 4;

    /// <summary>errno.h：EBADF = 9（darwin / FreeBSD / NetBSD 同值）。用于复现 Go「kq == -1 时 kevent 回 EBADF」。</summary>
    private const int _ebadf = 9;

    /// <summary>struct stat 缓冲大小（darwin 144、FreeBSD 224 字节，512 足够）。</summary>
    private const int _statBufSize = 512;

    // ---- 平台差异（.NET 没有 build tag）------------------------------------------

    /// <summary>
    /// .NET 的 <c>OperatingSystem</c> **没有** NetBSD 探测（只有 IsMacOS/IsFreeBSD/…，见 Watcher.cs 文件头第 10 条），
    /// 故 NetBSD 用 <c>RuntimeInformation.IsOSPlatform(OSPlatform.Create("NETBSD"))</c>——.NET 对未内置平台的标准写法
    /// （运行时的 OSPlatform 名 = RID 的平台前缀，NetBSD 构建上为 "NETBSD"）。任务书里的
    /// <c>OperatingSystem.IsNetBSD()</c> 在 net11.0 的引用程序集里不存在，编译不过。
    /// </summary>
    private static readonly bool _isNetBsd = RuntimeInformation.IsOSPlatform(OSPlatform.Create("NETBSD"));

    /// <summary>数组 stride = 该平台 `sizeof(struct kevent)`（见文件头第 3 条）。</summary>
    private static readonly int _keventSize =
        _isNetBsd ? _keventSizeNetBsd : OperatingSystem.IsFreeBSD() ? _keventSizeFreeBsd : _keventSizeDarwin;

    private static readonly int _keventFlagsOffset = _isNetBsd ? _keventFlagsOffsetNetBsd : _keventFlagsOffsetDarwin;
    private static readonly int _keventFflagsOffset = _isNetBsd ? _keventFflagsOffsetNetBsd : _keventFflagsOffsetDarwin;

    /// <summary>Go: <c>unix.EVFILT_READ</c>（netbsd 的头文件里是 0，其余为 -1）。</summary>
    private static readonly int _evFiltRead = _isNetBsd ? 0 : -1;

    /// <summary>Go: <c>unix.EVFILT_VNODE</c>（netbsd 的头文件里是 3，其余为 -4）。</summary>
    private static readonly int _evFiltVnode = _isNetBsd ? 3 : -4;

    /// <summary>Go: <c>openForEvents</c> 的 flags（darwin 用 O_EVTONLY，其余 BSD 用 O_RDONLY）。</summary>
    private static readonly int _openForEventsFlags = OperatingSystem.IsMacOS() ? _oEvtOnly : _oRdOnly;

    /// <summary>
    /// 是否启用 dev/ino 一致性检查（见文件头第 9 条）：**只**在 FreeBSD 上启用。
    ///
    /// macOS 上关闭的原因（M8FsWatch 集成时补）：本检查的字段偏移（st_dev int32 +0、st_ino uint64 +8）来自
    /// x/sys 的 <c>Stat_t</c> = 头文件里的 <c>_DARWIN_FEATURE_64_BIT_INODE</c>（INODE64）布局，而 64 位 macOS 上
    /// `fstat`/`lstat` 这两个**裸符号**是遗留的 INODE32 ABI（头文件把 `fstat` 宏成 `fstat$INODE64`）；
    /// 按名字 P/Invoke 会绑到遗留实现，读出来的 dev/ino 与偏移表不符 → 可能误判"路径已被替换"而反复摘挂 watch。
    /// 这段检查在 Go 注释里只服务于 **DragonFlyBSD** 的 rename-over 异常（本移植的 Default() 也不注册
    /// DragonFlyBSD），在 macOS 上它恒为 no-op，因此关闭它不改变可观察行为，却消除了一个未经验证的 ABI 依赖。
    /// </summary>
    private static readonly bool _supportsIdentityCheck = OperatingSystem.IsFreeBSD();

    /// <summary>NetBSD：已确认 libc 没有未版本化的 `kevent`，改用 `__kevent50`（见文件头第 4 条）。</summary>
    private static bool _netBsdKevent50;

    // ---- 实例状态 ---------------------------------------------------------------

    /// <summary>
    /// Go: <c>mu sync.Mutex</c>——kqueueBackend **自己的**锁（保护 subsByPath / fdToEntry / kq / fd 状态），
    /// 与基类里那个 watcherBase.mu 不是同一个（见文件头第 21 条）。
    /// </summary>
    private readonly object _kqMu = new();

    /// <summary>Go: <c>kq int</c>（-1 = 未创建或已关闭）。</summary>
    private int _kq = -1;

    /// <summary>Go: <c>pipeFDs [2]int</c>（-1 = 未创建或已关闭）。</summary>
    private readonly int[] _pipeFDs = [-1, -1];

    /// <summary>Go: <c>pipeWriteFD atomic.Int32</c>：只经 <c>Volatile</c>/<c>Interlocked</c> 访问。</summary>
    private int _pipeWriteFD = -1;

    /// <summary>Go: <c>subsByPath map[string][]*kqueueSubscription</c>（multimap&lt;path, sub&gt;）。</summary>
    private readonly Dictionary<string, List<KqueueSubscription>> _subsByPath = [];

    /// <summary>Go: <c>fdToEntry map[int]*dirEntry</c>。</summary>
    private readonly Dictionary<int, KqueueDirEntry> _fdToEntry = [];

    /// <summary>Go: <c>endedSignal chan struct{}</c>（close = 就绪）。</summary>
    private readonly TaskCompletionSource _endedSignal = new();

    /// <summary>Go: <c>watchersTouched map[*dirWatch]struct{}</c>：跨 pass 复用，只被 start 线程访问。</summary>
    private readonly HashSet<DirWatch> _watchersTouched = [];

    /// <summary>Go: <c>events := make([]unix.Kevent_t, 128)</c>：start 线程独享，循环退出后释放。</summary>
    private IntPtr _eventsBuf;

    /// <summary>
    /// Go 每次注册都现造 `[]unix.Kevent_t{ev}` → C# 复用**一个**单元素 changelist 缓冲（见文件头第 2 条）。
    /// 缓冲零初始化，所以 data/udata/ext 恒为 0——与 Go 的 <c>unix.SetKevent</c>（只写 ident/filter/flags）
    /// 作用在零值结构体上的效果一致；FreeBSD/NetBSD 的 `ext[4]` 也保持全 0。
    /// </summary>
    private IntPtr _changeBuf;

    /// <summary>Go 在 <c>compareDir</c> 里的两个栈上 <c>unix.Stat_t</c>（见文件头第 9 条）。</summary>
    private IntPtr _statBufFd;
    private IntPtr _statBufPath;

    /// <summary>Go: <c>func init() { kqueueWatcher.factory = func() watcherImpl { return newKqueueBackend() } }</c>。</summary>
    [SuppressMessage("Usage", "CA2255", Justification = "Go 的 kqueue.go init()：模块初始化器等价于「包被加载即注册」（见文件头第 23 条）。")]
    [ModuleInitializer]
    internal static void InitializeKqueueWatcher()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() || _isNetBsd)
        {
            FsWatch.KqueueWatcherInstance.Factory = static () => new KqueueBackend();
        }
    }

    /// <summary>Go: <c>func newKqueueBackend() *kqueueBackend</c>（-1 / 空表由字段初值承担）。</summary>
    internal KqueueBackend()
    {
        Init(this);
    }

    /// <summary>Go: <c>func (b *kqueueBackend) start() error</c>。由 <c>WatcherBase.Run()</c> 在后台线程调用。</summary>
    public override void Start()
    {
        var kq = Kqueue();
        if (kq < 0)
        {
            throw Wrap("unable to open kqueue", Marshal.GetLastPInvokeError());
        }

        _kq = kq;

        // Go 的 `defer func() { closeSubscriptions(); closeFDs(); close(endedSignal) }()`
        // （注册位置与 Go 一致：kqueue 建成之后、管道创建之前）。
        try
        {
            var fds = Marshal.AllocHGlobal(2 * sizeof(int));
            try
            {
                if (Pipe(fds) != 0)
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
            _changeBuf = AllocZeroed(_keventSize);

            // 把管道读端加进 kqueue，好在 shutdown 时打断循环（EVFILT_READ + EV_ADD|EV_CLEAR）。
            WriteChange(_pipeFDs[0], _evFiltRead, _evAdd | _evClear, 0);
            if (CallKevent(_kq, _changeBuf, 1, IntPtr.Zero, 0, IntPtr.Zero) < 0)
            {
                throw Wrap("unable to watch pipe", Marshal.GetLastPInvokeError());
            }

            NotifyStarted();

            _eventsBuf = AllocZeroed(_maxEvents * _keventSize);

            while (true)
            {
                var n = CallKevent(_kq, IntPtr.Zero, 0, _eventsBuf, _maxEvents, IntPtr.Zero);
                if (n < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno == _eintr)
                    {
                        continue;
                    }

                    throw Wrap("kevent error", errno);
                }

                var touched = _watchersTouched;
                var stop = false;
                for (var i = 0; i < n; i++)
                {
                    var fflags = ReadKeventFflags(i);
                    var flags = ReadKeventFlags(i);
                    var fd = ReadKeventIdent(i);
                    if (fd == _pipeFDs[0])
                    {
                        stop = true;
                        break;
                    }

                    // EV_ERROR：kevent 没能应用某条 changelist，或内核拒绝了注册，data 里是 errno。
                    // 此时 fflags 无意义，不作为普通事件分发（与 Go 的 continue 一致）。
                    if ((flags & _evError) != 0)
                    {
                        continue;
                    }

                    KqueueDirEntry? entry;
                    lock (_kqMu)
                    {
                        _fdToEntry.TryGetValue(fd, out entry);
                    }

                    if (entry is null)
                    {
                        continue;
                    }

                    if ((fflags & _noteWrite) != 0 && entry._isDir)
                    {
                        CompareDir(entry._path, touched);

                        // 目录上的 NOTE_WRITE 已经在上一行跑过 compareDir。DragonFlyBSD 的 rename-over 会把
                        // NOTE_DELETE 与父目录的 NOTE_WRITE 合并（而不是在被替换文件的 fd 上触发 NOTE_DELETE），
                        // 这里剥掉 NOTE_DELETE，免得把合并来的删除误判成目录自己被删。
                        fflags &= ~_noteDelete;
                    }

                    if ((fflags & ~_noteWrite) != 0 || !entry._isDir)
                    {
                        HandleFileEvent(fflags, entry, touched);
                    }
                }

                foreach (var w in touched)
                {
                    w.Notify();
                }

                touched.Clear();
                if (stop)
                {
                    break;
                }
            }
        }
        finally
        {
            CloseSubscriptions();
            CloseFDs();
            _endedSignal.TrySetResult();
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) closeFDs()</c>。事件循环退出之后执行；取 <c>_kqMu</c> 是为了与
    /// subscribe/closeWatch 对 <c>_kq</c> 的读取同步（见文件头第 22 条）。
    /// </summary>
    private void CloseFDs()
    {
        lock (_kqMu)
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

            if (_kq >= 0)
            {
                _ = Close(_kq);
                _kq = -1;
            }

            // Go 的 events/每次现造的 changelist 与栈上 Stat_t 分别是堆/栈内存；这里在事件循环确定退出后
            // 释放非托管缓冲（此刻不会再有注册、也不会有 compareDir，见文件头第 2、9 条）。
            FreeBuffer(ref _eventsBuf);
            FreeBuffer(ref _changeBuf);
            FreeBuffer(ref _statBufFd);
            FreeBuffer(ref _statBufPath);
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) closeSubscriptions()</c>。按 fd 去重后关闭每个订阅的 fd（同一个 fd 可能
    /// 被多个 path 的订阅引用），最后清空两张表。
    /// </summary>
    private void CloseSubscriptions()
    {
        lock (_kqMu)
        {
            var seenFDs = new HashSet<int>();
            foreach (var list in _subsByPath.Values)
            {
                foreach (var sub in list)
                {
                    if (sub._fd < 0 || !seenFDs.Add(sub._fd))
                    {
                        continue;
                    }

                    _ = Close(sub._fd);
                }
            }

            _subsByPath.Clear();
            _fdToEntry.Clear();
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) shutdown()</c>。向管道写一个字节唤醒 kevent 循环，再等它收尾
    /// （<c>pipeWriteFD &lt; 0</c> 时直接返回：Start 之前、或 kqueue 创建失败之后调用都不会卡住，与 Go 一致）。
    /// </summary>
    public override void Shutdown()
    {
        var fd = Volatile.Read(ref _pipeWriteFD);
        if (fd < 0)
        {
            return;
        }

        // Go: unix.Write(int(fd), []byte{'X'})（同样是每次调用一个 1 字节缓冲；管道阻塞，写入不会因容量失败）。
        var buf = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buf, 0, (byte)'X');
            _ = Write(fd, buf, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        _endedSignal.Task.Wait();
    }

    /// <summary>Go: <c>func (b *kqueueBackend) handleFileEvent(fflags uint32, entry *dirEntry, touched map[*dirWatch]struct{})</c>。</summary>
    private void HandleFileEvent(uint fflags, KqueueDirEntry entry, HashSet<DirWatch> touched)
    {
        lock (_kqMu)
        {
            var subs = FindSubscriptionsLocked(entry._path);

            if ((fflags & (_noteDelete | _noteRename | _noteRevoke)) != 0)
            {
                // 关掉过期的 fd：被监视的 inode 已经没了。
                if (entry._fd >= 0)
                {
                    _ = Close(entry._fd);
                    _fdToEntry.Remove(entry._fd);
                    entry._fd = -1;
                }

                var recreated = false;
                if ((fflags & _noteDelete) != 0 && (fflags & (_noteRename | _noteRevoke)) == 0 && !entry._isDir)
                {
                    recreated = TryRewatchLocked(entry);
                }

                foreach (var sub in subs)
                {
                    touched.Add(sub._dirWatch);
                    if (recreated)
                    {
                        sub._dirWatch._events.Update(sub._path);
                    }
                    else
                    {
                        sub._dirWatch._events.Remove(sub._path);

                        // 丢的是一个目录：把它所有后代的 fd 也关掉。部分内核（尤其 OpenBSD）只投递父目录的
                        // NOTE_DELETE/NOTE_RENAME，从不在子项上触发 NOTE_DELETE；不清理的话，之后修改被移走
                        // 的那棵树里的文件会按后代那条**改名前的旧路径**冒出来。同时为每个被关掉的后代发一次
                        // delete，内核没逐个投递时调用方也不会漏（内核投递了的话，后续 handleFileEvent 会发现
                        // fd 已经消失、成为 no-op，eventList 的合并负责去重）。
                        if (entry._isDir)
                        {
                            CloseDescendantFDsLocked(sub._dirWatch, sub._entries, sub._path);
                        }

                        RemoveEntryAndDescendants(sub._entries, sub._path);

                        // watch 根自己被删：这个 dirWatch 不会再有任何事件了，告诉调用方。
                        if (sub._path == sub._dirWatch._dir)
                        {
                            sub._dirWatch._events.SetError(
                                FsWatchException.WatchTerminated("fswatch: watch terminated: watched directory removed"));
                        }
                    }
                }

                if (!recreated)
                {
                    _subsByPath.Remove(entry._path);
                }

                return;
            }

            foreach (var sub in subs)
            {
                touched.Add(sub._dirWatch);
                if ((fflags & (_noteWrite | _noteAttrib | _noteExtend)) != 0)
                {
                    sub._dirWatch._events.Update(sub._path);
                }
            }
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) closeDescendantFDsLocked(w *dirWatch, entries map[string]*dirEntry, root string)</c>。
    /// 关闭 root **严格之下**每个条目的 fd、摘掉对应的 subsByPath/fdToEntry 簿记，并为每个后代发一次 delete。
    /// 用于「目录的父级丢了、而内核没把丢失传播给子项」的场景；eventList 会与之后到达的 per-child
    /// NOTE_DELETE 合并。
    /// </summary>
    private void CloseDescendantFDsLocked(DirWatch w, Dictionary<string, KqueueDirEntry> entries, string root)
    {
        var prefix = root + Path.DirectorySeparatorChar;
        foreach (var (path, e) in entries)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (e._fd >= 0)
            {
                _ = Close(e._fd);
                _fdToEntry.Remove(e._fd);
                e._fd = -1;
            }

            _subsByPath.Remove(path);
            w._events.Remove(path);
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) tryRewatchLocked(entry *dirEntry) bool</c>。被删的路径是否立刻被「同类型」
    /// 重建？是就开新 fd、注册 kqueue watch 并返回 true（调用方应发 update 而不是 delete）。
    /// </summary>
    private bool TryRewatchLocked(KqueueDirEntry entry)
    {
        // Go: unix.Lstat(entry.watchPath, &st) 失败即 false；st.Mode & S_IFMT == S_IFDIR 判类型 → 文件头第 8 条。
        if (FsWatch.IsDirectoryNoFollow(entry._watchPath) != entry._isDir)
        {
            return false;
        }

        if (!TryOpenForEvents(entry._watchPath, out var fd))
        {
            return false;
        }

        if (!TryRegisterVnode(fd))
        {
            _ = Close(fd);
            return false;
        }

        entry._fd = fd;
        _fdToEntry[fd] = entry;
        return true;
    }

    /// <summary>Go: <c>func (b *kqueueBackend) closeEntryLocked(entry *dirEntry)</c>。</summary>
    private void CloseEntryLocked(KqueueDirEntry entry)
    {
        if (entry._fd >= 0)
        {
            _ = Close(entry._fd);
            _fdToEntry.Remove(entry._fd);
            entry._fd = -1;
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) removeSubsForEntriesLocked(path string, entriesPtr *map[string]*dirEntry)</c>。
    /// Go 用 map 指针的**身份**判断订阅属于哪张 entries 表 → C# 比较 Dictionary 实例引用。
    /// </summary>
    private void RemoveSubsForEntriesLocked(string path, Dictionary<string, KqueueDirEntry> entries)
    {
        if (!_subsByPath.TryGetValue(path, out var list))
        {
            return; // Go: 不存在的键取出 nil 切片、压缩后仍为空 → delete 是 no-op
        }

        _ = list.RemoveAll(s => ReferenceEquals(s._entries, entries));
        if (list.Count == 0)
        {
            _subsByPath.Remove(path);
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) removeEntryAndDescendantsLocked(entriesPtr *map[string]*dirEntry, path string, includeRoot bool)</c>。
    /// </summary>
    private void RemoveEntryAndDescendantsLocked(
        Dictionary<string, KqueueDirEntry> entries,
        string path,
        bool includeRoot)
    {
        // Go 允许在 range map 期间 delete；C# 不允许 → 先收集要删的键（判定只看 key 内容，见文件头第 12 条）。
        var toRemove = new List<string>();
        foreach (var descendant in entries.Keys)
        {
            if (descendant == path)
            {
                if (!includeRoot)
                {
                    continue;
                }
            }
            else if (!IsStrictDescendant(descendant, path))
            {
                continue;
            }

            toRemove.Add(descendant);
        }

        foreach (var descendant in toRemove)
        {
            CloseEntryLocked(entries[descendant]);
            RemoveSubsForEntriesLocked(descendant, entries);
            entries.Remove(descendant);
        }
    }

    /// <summary>Go: <c>func (b *kqueueBackend) findSubscriptionsLocked(path string) []*kqueueSubscription</c>（返回副本）。</summary>
    private List<KqueueSubscription> FindSubscriptionsLocked(string path) =>
        _subsByPath.TryGetValue(path, out var subs) ? [.. subs] : [];

    /// <summary>Go: <c>func (b *kqueueBackend) subscribe(w *dirWatch) error</c>。经 watchAdd 在 watcherBase.mu 下调用。</summary>
    public override void Subscribe(DirWatch w)
    {
        // 先在**不发布**任何 watch/订阅的情况下建 entries 表，避免数据竞争：一旦通过 subsByPath 发布，
        // 事件循环就可能读它（compareDir），而那时表还没填完。
        var entries = new Dictionary<string, KqueueDirEntry>();
        FsWatch.WalkDir(w._physicalDir, w._recursive, (watchPath, isDir) =>
        {
            var path = w.DisplayPath(watchPath);
            entries[path] = new KqueueDirEntry(path, watchPath, isDir);
        });

        // 开 fd、注册 kevent、发布订阅全部在 _kqMu 之下：保证事件循环看不到半成品 entries 表，
        // 也保证任何提前返回都不会漏掉 fd 簿记（fdToEntry 里一定记着）。
        lock (_kqMu)
        {
            // Go 在 range entries 期间 delete(entries, key)；C# 不允许 → 对**键**做快照再遍历。
            // 等价：删除只发生在本轮正在处理的那个键上（不会删掉尚未访问的键，见文件头第 12 条）。
            foreach (var path in new List<string>(entries.Keys))
            {
                var entry = entries[path];
                if (!TryOpenForEvents(entry._watchPath, out var fd))
                {
                    // 紧随失败的 open 读取 errno（TryOpenForEvents 只做一次 open，没有被其它 P/Invoke 覆盖）。
                    var errno = Marshal.GetLastPInvokeError();
                    if (path == w._dir)
                    {
                        CleanupEntriesLocked(entries);
                        throw new DirWatchError(Wrap($"error watching {w._dir}", errno), w);
                    }

                    entries.Remove(path);
                    continue;
                }

                if (!TryRegisterVnode(fd))
                {
                    var errno = Marshal.GetLastPInvokeError();
                    _ = Close(fd);
                    if (path == w._dir)
                    {
                        CleanupEntriesLocked(entries);
                        throw new DirWatchError(Wrap($"error watching {w._dir}", errno), w);
                    }

                    entries.Remove(path);
                    continue;
                }

                entry._fd = fd;
                _fdToEntry[fd] = entry;
            }

            foreach (var (path, entry) in entries)
            {
                var sub = new KqueueSubscription(w, path, entries) { _fd = entry._fd };
                if (!_subsByPath.TryGetValue(path, out var list))
                {
                    list = [];
                    _subsByPath[path] = list;
                }

                list.Add(sub);
            }
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) cleanupEntriesLocked(entries map[string]*dirEntry)</c>。subscribe 失败时
    /// 关掉所有已打开的 fd，避免泄漏。必须在 <c>_kqMu</c> 之下调用。
    /// </summary>
    private void CleanupEntriesLocked(Dictionary<string, KqueueDirEntry> entries)
    {
        foreach (var e in entries.Values)
        {
            CloseEntryLocked(e);
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) watchPath(w *dirWatch, path string, entries map[string]*dirEntry) bool</c>
    /// （对应注释里的 <c>kqueueBackend::watchDir</c>）。
    /// </summary>
    private bool WatchPath(DirWatch w, string path, Dictionary<string, KqueueDirEntry> entries)
    {
        if (!entries.TryGetValue(path, out var entry))
        {
            return false;
        }

        lock (_kqMu)
        {
            var sub = new KqueueSubscription(w, path, entries);
            if (entry._fd < 0)
            {
                if (!TryOpenForEvents(entry._watchPath, out var fd))
                {
                    return false;
                }

                if (!TryRegisterVnode(fd))
                {
                    _ = Close(fd);
                    return false;
                }

                entry._fd = fd;
                _fdToEntry[fd] = entry;
            }

            sub._fd = entry._fd;
            if (!_subsByPath.TryGetValue(path, out var list))
            {
                list = [];
                _subsByPath[path] = list;
            }

            list.Add(sub);
            return true;
        }
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) compareDir(_ int, path string, touched map[*dirWatch]struct{}) bool</c>
    /// （第一个 int 参数是 fd，Go 用 <c>_</c> 丢弃 → C# 不设该参数）。被监视目录上的 NOTE_WRITE 触发：
    /// 重新列目录、与内存树 diff，为新增项发 create（并开 fd 注册 watch）、为消失项发 delete（并关 fd）。
    /// </summary>
    private bool CompareDir(string path, HashSet<DirWatch> touched)
    {
        List<KqueueSubscription> subs;
        lock (_kqMu)
        {
            subs = FindSubscriptionsLocked(path);
        }

        // 非递归订阅只对根目录做 compareDir：子目录上的 NOTE_WRITE 说明它内部有变化，但非递归模式不该报告
        // 这些变化 —— 只为子目录本身发一次 update（它的元数据变了）然后返回。
        var filteredSubs = new List<KqueueSubscription>();
        foreach (var s in subs)
        {
            if (!s._dirWatch._recursive && path != s._dirWatch._dir)
            {
                s._dirWatch._events.Update(path);
                touched.Add(s._dirWatch);
            }
            else
            {
                filteredSubs.Add(s);
            }
        }

        if (filteredSubs.Count == 0)
        {
            return true;
        }

        subs = filteredSubs;

        var dirStart = path + Path.DirectorySeparatorChar;
        var snapshots = new Dictionary<string, DiskSnapshot>();

        // 每个订阅有自己的 entries 表（subscribe 里建的）。同一路径上的多个订阅来自覆盖重叠子树的多个
        // dirWatch；它们的表一定互不相同，所以直接遍历 subs，而不去按 map 身份去重。
        foreach (var sub in subs)
        {
            if (!sub._entries.TryGetValue(path, out var baseEntry))
            {
                continue;
            }

            var watchPath = baseEntry._watchPath;
            var watchDirStart = watchPath + Path.DirectorySeparatorChar;

            if (!snapshots.TryGetValue(watchPath, out var snapshot))
            {
                if (!TryReadEntries(watchPath, out var diskEntries))
                {
                    continue;
                }

                snapshot = new DiskSnapshot(diskEntries);
                foreach (var ent in diskEntries)
                {
                    snapshot.CurrentDisplayPaths.Add(dirStart + ent.Name);
                }

                snapshots[watchPath] = snapshot;
            }

            var entries = sub._entries;
            foreach (var ent in snapshot.Entries)
            {
                var fullPath = dirStart + ent.Name;
                var fullWatchPath = watchDirStart + ent.Name;

                if (entries.TryGetValue(fullPath, out var existing))
                {
                    // fd 是否还指向磁盘上同一个 inode？DragonFlyBSD 的 rename-over 不会在被替换文件的 fd 上
                    // 触发 NOTE_DELETE，于是留下一个 fd 指向旧（已 unlink）inode 的僵尸条目。
                    if (existing._fd >= 0 && TryPathReplaced(existing._fd, fullWatchPath))
                    {
                        lock (_kqMu)
                        {
                            CloseEntryLocked(existing);
                            RemoveSubsForEntriesLocked(fullPath, sub._entries);
                            if (existing._isDir)
                            {
                                RemoveEntryAndDescendantsLocked(sub._entries, fullPath, false);
                            }

                            existing._isDir = ent.IsDir;
                        }
                    }

                    if (existing._fd >= 0)
                    {
                        continue;
                    }

                    // 条目还在但 fd 过期了（文件被替换）：重新监视并发一次 update。
                    if (!WatchPath(sub._dirWatch, fullPath, entries))
                    {
                        continue;
                    }

                    sub._dirWatch._events.Update(fullPath);
                    touched.Add(sub._dirWatch);
                    if (ent.IsDir && sub._dirWatch._recursive)
                    {
                        WatchTree(sub._dirWatch, fullWatchPath, entries);
                    }

                    continue;
                }

                var e = new KqueueDirEntry(fullPath, fullWatchPath, ent.IsDir);
                entries[fullPath] = e;
                if (!WatchPath(sub._dirWatch, fullPath, entries))
                {
                    entries.Remove(fullPath);
                    continue;
                }

                sub._dirWatch._events.Create(fullPath);
                touched.Add(sub._dirWatch);

                // 递归订阅：走进新目录，捞到预先就存在的子目录（例如整棵树被移进被监视区）。
                if (ent.IsDir && sub._dirWatch._recursive)
                {
                    WatchTree(sub._dirWatch, fullWatchPath, entries);
                }
            }

            // 找删除：dirStart 直接之下、磁盘上已经不存在的条目。
            var toRemove = new List<string>();
            foreach (var p in entries.Keys)
            {
                if (!p.StartsWith(dirStart, StringComparison.Ordinal))
                {
                    continue;
                }

                var rest = p[dirStart.Length..];
                if (rest.Contains(Path.DirectorySeparatorChar))
                {
                    continue;
                }

                if (snapshot.CurrentDisplayPaths.Contains(p))
                {
                    continue;
                }

                toRemove.Add(p);
            }

            foreach (var p in toRemove)
            {
                sub._dirWatch._events.Remove(p);
                touched.Add(sub._dirWatch);
                lock (_kqMu)
                {
                    // Go 在这里**不**清 e.state、也不动 entries（随后由 removeEntryAndDescendants 统一删），
                    // 只关 fd 并摘掉 fdToEntry/subsByPath → 逐行照搬。
                    foreach (var (descendant, dead) in entries)
                    {
                        if (descendant != p && !IsStrictDescendant(descendant, p))
                        {
                            continue;
                        }

                        if (dead._fd >= 0)
                        {
                            _ = Close(dead._fd);
                            _fdToEntry.Remove(dead._fd);
                        }

                        _subsByPath.Remove(descendant);
                    }
                }

                RemoveEntryAndDescendants(entries, p);
            }
        }

        return true;
    }

    /// <summary>
    /// Go: compareDir 里的 <c>_ = walkDir(fullWatchPath, true, func(p string, pIsDir bool) error {...})</c>。
    /// 跳过根（调用方已处理），为每个后代建条目、发 create 并注册 watch。
    /// </summary>
    private void WatchTree(DirWatch w, string fullWatchPath, Dictionary<string, KqueueDirEntry> entries)
    {
        try
        {
            FsWatch.WalkDir(fullWatchPath, true, (p, pIsDir) =>
            {
                if (p == fullWatchPath)
                {
                    return; // 调用方已处理
                }

                var displayPath = w.DisplayPath(p);
                entries[displayPath] = new KqueueDirEntry(displayPath, p, pIsDir);
                w._events.Create(displayPath);
                _ = WatchPath(w, displayPath, entries);
            });
        }
        catch (IOException)
        {
            // Go 的 `_ = walkDir(...)` 丢弃错误（见文件头第 11 条）。
        }
    }

    /// <summary>
    /// Go: <c>func readEntries(path string) ([]os.DirEntry, error)</c>（= <c>os.ReadDir</c>）。返回按文件名排序的
    /// (名字, 是否目录) 快照（见文件头第 10 条）。
    /// </summary>
    private static bool TryReadEntries(string path, out DiskEntry[] entries)
    {
        entries = [];
        string[] paths;
        try
        {
            paths = Directory.GetFileSystemEntries(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // Go: os.ReadDir 返回 err（路径消失 / 不是目录 / 无权限）
        }

        Array.Sort(paths, static (a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));

        var result = new DiskEntry[paths.Length];
        for (var i = 0; i < paths.Length; i++)
        {
            result[i] = new DiskEntry(Path.GetFileName(paths[i]), FsWatch.IsDirectoryNoFollow(paths[i]));
        }

        entries = result;
        return true;
    }

    /// <summary>
    /// Go: <c>func (b *kqueueBackend) closeWatch(w *dirWatch) error</c>。摘掉 w 在所有路径上的订阅；某个路径的
    /// 订阅全被摘掉时就关掉该路径第一个订阅的 fd（Go 读的是 <c>list[0].fd</c>，见文件头第 13 条）——
    /// 「关掉文件描述符就等于在 kqueue 里取消监视」。
    /// </summary>
    public override void CloseWatch(DirWatch w)
    {
        lock (_kqMu)
        {
            // Go 在 range b.subsByPath 期间 delete；C# 不允许 → 对键做快照（见文件头第 12 条）。
            foreach (var path in new List<string>(_subsByPath.Keys))
            {
                var list = _subsByPath[path];
                var firstFD = list.Count > 0 ? list[0]._fd : -1; // Go 在 kept 为空时读到的就是原 list[0]
                var removedAny = false;
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(list[i]._dirWatch, w))
                    {
                        list.RemoveAt(i);
                        removedAny = true;
                    }
                }

                if (!removedAny)
                {
                    continue;
                }

                if (list.Count == 0)
                {
                    _ = Close(firstFD);
                    _fdToEntry.Remove(firstFD);
                    _subsByPath.Remove(path);
                }
            }
        }
    }

    /// <summary>Go: <c>func removeEntryAndDescendants(entries map[string]*dirEntry, path string)</c>（包级函数，无锁）。</summary>
    private static void RemoveEntryAndDescendants(Dictionary<string, KqueueDirEntry> entries, string path)
    {
        entries.Remove(path);

        // Go 允许在 range map 期间 delete；C# 不允许 → 先收集键（见文件头第 12 条）。
        var toRemove = new List<string>();
        foreach (var k in entries.Keys)
        {
            if (IsStrictDescendant(k, path))
            {
                toRemove.Add(k);
            }
        }

        foreach (var k in toRemove)
        {
            entries.Remove(k);
        }
    }

    /// <summary>
    /// Go 的内联判定 <c>len(k) &gt; len(path) &amp;&amp; k[len(path)] == filepath.Separator &amp;&amp; k[:len(path)] == path</c>。
    /// 按 UTF-16 代码单元下标实现：'/' 是 ASCII、不会落在代理对中间，故与 Go 的字节下标判定等价。
    /// </summary>
    private static bool IsStrictDescendant(string candidate, string path) =>
        candidate.Length > path.Length
        && candidate[path.Length] == Path.DirectorySeparatorChar
        && candidate.StartsWith(path, StringComparison.Ordinal);

    /// <summary>
    /// Go: <c>unix.Fstat(fd) == nil &amp;&amp; unix.Lstat(path) == nil</c> 之后的 <c>Dev</c>/<c>Ino</c> 比较
    /// （见文件头第 9 条：只在 macOS/FreeBSD 上启用）。
    /// </summary>
    private bool TryPathReplaced(int fd, string path) =>
        _supportsIdentityCheck
        && TryGetFdIdentity(fd, out var fdDev, out var fdIno)
        && TryGetPathIdentity(path, out var pathDev, out var pathIno)
        && (fdDev != pathDev || fdIno != pathIno);

    /// <summary>Go: <c>unix.Fstat(fd, &amp;fdSt) == nil</c>。</summary>
    private bool TryGetFdIdentity(int fd, out long dev, out long ino)
    {
        EnsureStatBuffers();
        dev = 0;
        ino = 0;
        return Fstat(fd, _statBufFd) == 0 && TryReadStatIdentity(_statBufFd, out dev, out ino);
    }

    /// <summary>Go: <c>unix.Lstat(path, &amp;pathSt) == nil</c>（**不**跟随符号链接）。</summary>
    private bool TryGetPathIdentity(string path, out long dev, out long ino)
    {
        EnsureStatBuffers();
        dev = 0;
        ino = 0;
        return Lstat(path, _statBufPath) == 0 && TryReadStatIdentity(_statBufPath, out dev, out ino);
    }

    /// <summary>
    /// struct stat 里只读这两个字段（见文件头第 9 条）：
    ///   darwin ：st_dev int32 +0、st_ino uint64 +8
    ///   freebsd：st_dev uint64 +0、st_ino uint64 +8
    /// 调用方由 <c>_supportsIdentityCheck</c> 守卫，故只需区分这两者。
    /// </summary>
    private static bool TryReadStatIdentity(IntPtr buf, out long dev, out long ino)
    {
        dev = OperatingSystem.IsMacOS() ? Marshal.ReadInt32(buf, 0) : Marshal.ReadInt64(buf, 0);
        ino = Marshal.ReadInt64(buf, 8);
        return true;
    }

    /// <summary>Go 在 compareDir 里用的是**栈上** <c>unix.Stat_t</c>；C# 取不到栈地址 → 惰性分配一次（文件头第 9 条）。</summary>
    private void EnsureStatBuffers()
    {
        if (_statBufFd == IntPtr.Zero)
        {
            _statBufFd = AllocZeroed(_statBufSize);
            _statBufPath = AllocZeroed(_statBufSize);
        }
    }

    /// <summary>
    /// Go: <c>type diskSnapshot struct { entries []os.DirEntry; currentDisplayPaths map[string]struct{} }</c>
    /// （compareDir 内的局部类型 → 私有嵌套类；Go 侧只共享 <c>currentDisplayPaths</c> 这个 map 引用，类是等价映射）。
    /// </summary>
    private sealed class DiskSnapshot(DiskEntry[] entries)
    {
        internal DiskEntry[] Entries { get; } = entries;

        internal HashSet<string> CurrentDisplayPaths { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Go: <c>os.DirEntry</c> 在本文件里用到的两个成员（<c>Name()</c> / <c>IsDir()</c>）。</summary>
    private readonly record struct DiskEntry(string Name, bool IsDir);

    // ---- kevent 缓冲与注册辅助 ---------------------------------------------------

    /// <summary>分配一块**零初始化**的非托管缓冲（对应 Go 的 <c>make([]T)</c> / 结构体零值）。</summary>
    private static IntPtr AllocZeroed(int bytes)
    {
        var buf = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(new byte[bytes], 0, buf, bytes);
        return buf;
    }

    private static void FreeBuffer(ref IntPtr buf)
    {
        if (buf != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buf);
            buf = IntPtr.Zero;
        }
    }

    /// <summary>
    /// 写一条 changelist 记录（等价 Go 的 <c>unix.SetKevent(&amp;ev, ident, filter, flags)</c> 加
    /// <c>ev.Fflags = fflags</c>）。只写 ident/filter/flags/fflags：其余字段保持分配时的 0，与 Go 在零值
    /// <c>Kevent_t</c> 上只设这几个字段一致（data/udata 从未被 Go 读过，ext[] 也保持 0）。
    /// </summary>
    private void WriteChange(int ident, int filter, uint flags, uint fflags)
    {
        Marshal.WriteInt64(_changeBuf, _keventIdentOffset, ident);

        if (_isNetBsd)
        {
            // netbsd：filter/flags/fflags 都是 uint32（见文件头第 3 条）。
            Marshal.WriteInt32(_changeBuf, _keventFilterOffset, filter);
            Marshal.WriteInt32(_changeBuf, _keventFlagsOffset, unchecked((int)flags));
        }
        else
        {
            Marshal.WriteInt16(_changeBuf, _keventFilterOffset, unchecked((short)filter));
            Marshal.WriteInt16(_changeBuf, _keventFlagsOffset, unchecked((short)flags));
        }

        Marshal.WriteInt32(_changeBuf, _keventFflagsOffset, unchecked((int)fflags));
    }

    /// <summary>Go: <c>fd := int(events[i].Ident)</c>（内核写回的就是被监视对象的 fd）。</summary>
    private int ReadKeventIdent(int index) =>
        unchecked((int)Marshal.ReadInt64(_eventsBuf, (index * _keventSize) + _keventIdentOffset));

    /// <summary>Go: <c>flags := events[i].Flags</c>（netbsd 是 uint32，其余是 uint16）。</summary>
    private uint ReadKeventFlags(int index) =>
        _isNetBsd
            ? unchecked((uint)Marshal.ReadInt32(_eventsBuf, (index * _keventSize) + _keventFlagsOffset))
            : unchecked((uint)Marshal.ReadInt16(_eventsBuf, (index * _keventSize) + _keventFlagsOffset));

    /// <summary>Go: <c>fflags := events[i].Fflags</c>。</summary>
    private uint ReadKeventFflags(int index) =>
        unchecked((uint)Marshal.ReadInt32(_eventsBuf, (index * _keventSize) + _keventFflagsOffset));

    /// <summary>Go: <c>func openForEvents(path string) (int, error)</c>（见文件头第 7 条）。</summary>
    private static bool TryOpenForEvents(string path, out int fd)
    {
        fd = Open(path, _openForEventsFlags, 0);
        return fd >= 0;
    }

    /// <summary>Go: 三处逐字重复的 EVFILT_VNODE 注册块（见文件头第 17 条）。失败时调用方读 errno。</summary>
    private bool TryRegisterVnode(int fd)
    {
        // Go 在 Start 之前（或 shutdown 之后）kq == -1 时照样调 unix.Kevent(-1, …)，内核回 EBADF，不会崩；
        // C# 不能拿 _changeBuf == IntPtr.Zero 去解引用（AccessViolation 是致命的），所以显式复现同一个 errno。
        if (_kq < 0 || _changeBuf == IntPtr.Zero)
        {
            Marshal.SetLastPInvokeError(_ebadf);
            return false;
        }

        WriteChange(fd, _evFiltVnode, _evAdd | _evClear | _evEnable, _vnodeNotes);
        return CallKevent(_kq, _changeBuf, 1, IntPtr.Zero, 0, IntPtr.Zero) >= 0;
    }

    /// <summary>Go: <c>unix.Kevent(...)</c>。NetBSD 的符号名回退见文件头第 4 条。</summary>
    private static int CallKevent(int kq, IntPtr changelist, int nchanges, IntPtr eventlist, int nevents, IntPtr timeout)
    {
        if (!_isNetBsd)
        {
            return Kevent(kq, changelist, nchanges, eventlist, nevents, timeout);
        }

        if (!_netBsdKevent50)
        {
            try
            {
                return Kevent(kq, changelist, nchanges, eventlist, nevents, timeout);
            }
            catch (EntryPointNotFoundException)
            {
                // libc 没有导出未版本化的 `kevent`（只有 __kevent50）→ 永久切到后者。
                _netBsdKevent50 = true;
            }
        }

        return KeventNetBsd50(kq, changelist, nchanges, eventlist, nevents, timeout);
    }

    // ---- errno 辅助 -------------------------------------------------------------

    /// <summary>Go 的 <c>fmt.Errorf("…: %w", err)</c>：消息与 Go 一致，errno 放进 InnerException（文件头第 20 条）。</summary>
    private static IOException Wrap(string message, int errno) =>
        new($"{message}: {ErrnoText(errno)}", new Win32Exception(errno));

    private static string ErrnoText(int errno) => new Win32Exception(errno).Message;

    // ---- libc（darwin libSystem / FreeBSD、NetBSD libc；全部 SetLastError = true）--

    [DllImport("libc", EntryPoint = "kqueue", SetLastError = true)]
    private static extern int Kqueue();

    [DllImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static extern int Kevent(
        int kq,
        IntPtr changelist,
        int nchanges,
        IntPtr eventlist,
        int nevents,
        IntPtr timeout);

    /// <summary>NetBSD 9/10 头文件里 <c>kevent</c> 的真名（见文件头第 4 条）。</summary>
    [DllImport("libc", EntryPoint = "__kevent50", SetLastError = true)]
    private static extern int KeventNetBsd50(
        int kq,
        IntPtr changelist,
        int nchanges,
        IntPtr eventlist,
        int nevents,
        IntPtr timeout);

    [DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static extern int Pipe(IntPtr pipefd);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint Write(int fd, IntPtr buf, nuint count);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int Lstat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buf);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int Fstat(int fd, IntPtr buf);
}

/// <summary>Go: <c>type dirEntry struct</c>。kqueue 的 fd↔path 映射里的一条记录。</summary>
internal sealed class KqueueDirEntry
{
    internal string _path;
    internal string _watchPath;
    internal bool _isDir;

    /// <summary>Go: <c>state any</c>（nil = 未打开；int = 已打开的 fd）→ -1 = 未打开（文件头第 6 条）。</summary>
    internal int _fd = -1;

    internal KqueueDirEntry(string path, string watchPath, bool isDir)
    {
        _path = path;
        _watchPath = watchPath;
        _isDir = isDir;
    }
}

/// <summary>
/// Go: <c>type kqueueSubscription struct</c>。一次订阅；<c>_entries</c> 是**该 dirWatch 自己的**那棵 path→dirEntry
/// 表（Go 用 map 指针身份区分，C# 用 Dictionary 引用相等）。
/// </summary>
internal sealed class KqueueSubscription
{
    internal DirWatch _dirWatch;
    internal string _path;
    internal Dictionary<string, KqueueDirEntry> _entries;
    internal int _fd = -1;

    internal KqueueSubscription(DirWatch dirWatch, string path, Dictionary<string, KqueueDirEntry> entries)
    {
        _dirWatch = dirWatch;
        _path = path;
        _entries = entries;
    }
}
