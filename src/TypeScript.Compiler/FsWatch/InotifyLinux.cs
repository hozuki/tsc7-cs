// ported from: internal/fswatch/inotify_linux.go:1-427
//
// C# 映射与偏离（逐条）：
// 1) Go 用 `golang.org/x/sys/unix` 的封装（Pipe2/InotifyInit1/InotifyAddWatch/InotifyRmWatch/Poll/Read/Write/Close）
//    → C# 直接 `DllImport("libc")` 同名符号（全部 `SetLastError = true`，errno 取 `Marshal.GetLastPInvokeError()`）。
//    **不使用 `unsafe`**（本项目未开 AllowUnsafeBlocks）：pollfd 与 inotify_event 都在 `Marshal.AllocHGlobal`
//    出来的非托管缓冲上按字段偏移读写（`Marshal.ReadInt32/ReadInt16/WriteInt32/WriteInt16`）。
// 2) Go 的 `(*unix.InotifyEvent)(unsafe.Pointer(&buf[offset]))` 把读缓冲重解释成结构体指针 → C# 逐字段取值。
//    **结构体偏移表（唯一支持的目标：Linux x86-64 / arm64，两者 ABI 相同）**：
//      struct inotify_event { int32 wd; uint32 mask; uint32 cookie; uint32 len; char name[]; }
//        wd = +0, mask = +4, cookie = +8（Go 也不读）, len = +12, name = +16；头长 16（= unix.SizeofInotifyEvent）。
//      struct pollfd { int fd; short events; short revents; }
//        fd = +0, events = +4, revents = +6；长 8（故第 n 个元素的基址 = n * 8）。
//    （这两个布局在所有 Linux 架构上一致——inotify_event 全是 4 字节对齐成员，pollfd 是 int/short/short。）
// 3) Go 的 `ev.Len`（uint32）→ `recordSize = unix.SizeofInotifyEvent + int(ev.Len)`，C# 同样先把 uint32 读出来
//    再转 int（不省这一步，避免 max-uint 的病态事件把偏移算错方向）。
// 4) 名字是 NUL 结尾、`len` 可能含 NUL 填充：Go 手动截到第一个 NUL 字节再 `string(nameBytes)`（非法 UTF-8 →
//    U+FFFD）→ C# 用 `Marshal.PtrToStringUTF8`（读到首个 NUL 为止，非法序列同样替换 U+FFFD），语义相同。
// 5) Go 的 `b.pipeWriteFD atomic.Int32`（让 shutdown 能安全对抗 start goroutine 的 deferred closeFDs）→
//    C# 普通字段 `_pipeWriteFD` + `Volatile.Read` / `Interlocked.Exchange`（= Go 的 Load / Swap）。
//    Go 构造函数里的 `Store(-1)` 由字段初始值 `-1` 承担。
// 6) Go 的 `chan struct{} endedSignal`（close = 就绪）→ `TaskCompletionSource`；`close(b.endedSignal)` →
//    `TrySetResult()`（幂等，Go 的 close 二次调用会 panic，这里不会）。
// 7) Go 的 `defer func() { b.closeFDs(); close(b.endedSignal) }()` → `Start()` 的 `try/finally`，
//    执行顺序一致（先关 fd、再放行 shutdown 的等待者）。
// 8) Go 的 `map[int][]*inotifySubscription` → `Dictionary<int, List<InotifySubscription>>`；
//    `map[*dirWatch]struct{}` → `HashSet<DirWatch>`（引用相等语义一致）。
// 9) Go 的 `for _, s := range b.subscriptions[wd]`（range 用循环开始时的 len）在 `handleSubscription` 的删除
//    分支里会被原地压缩（`kept := list[:0]` 写回同一个切片），因此 Go 可能重复处理被搬移到尾部的元素。
//    C# 不允许在 foreach 期间修改 List → `HandleEvent` 改为**按下标**遍历同一个 List 实例（`list.Count` 每次
//    重读，`var s = list[i]` 先取值再调用，等价 Go 的 range 变量拷贝），删除分支用 `List.RemoveAll` 原地压缩。
//    唯一差异：Go 可能重复处理的那个尾部元素，C# 只处理一次（更接近 Go 注释所述的 C++ 语义：按快照遍历 + erase）。
// 10) Go 在 `range map` 的同时 `delete(map, wd)` 是合法的；C# 的 Dictionary 不允许 → 先 `new List<int>(Keys)`
//     快照键（只是键的快照，值的读写仍在同一个字典上，最终状态与 Go 相同）。
// 11) Go 的 `switch { case <bool>: }` → `if / else if`（求值顺序与只执行首个匹配分支的语义相同）。
// 12) errno：Go 的 `errors.Is(err, unix.EAGAIN / unix.EWOULDBLOCK / unix.EINTR)` → C# 直接比较
//     `Marshal.GetLastPInvokeError()`。Linux 上 EWOULDBLOCK == EAGAIN == 11，故 Go 的双重判定退化为单次比较。
// 13) Go 的 `fmt.Errorf("…: %w", err)` → C# `new IOException($"…: {strerror(errno)}", new Win32Exception(errno))`
//     （消息文本与 Go 一致，errno 通过 InnerException.NativeErrorCode 可见）。Go 的裸 errno 错误（`errno.Errno`）
//     → `ErrnoErr`（消息只有 strerror 文本）。
// 14) Go 的字符串前缀判定 `s.path[:len(path)] == path && s.path[len(path)] == '/'` 在 C# 里按 UTF-16 代码单元
//     下标实现（见 `IsPathOrDescendant`）。'/' 是 ASCII、不会落在代理对中间，故与 Go 的字节下标判定等价。
//     **不复用** `FsWatch.IsInDirectoryOrSelf`：那条 helper 对以分隔符结尾的 dir（例如 "/"）会返回 true，
//     而这里的内联表达式不会 —— 本文件按 Go 的表达式逐字移植。
// 15) Go 的 `_, _ = b.watchDir(...)` / `_ = walkDir(...)`（丢弃错误）→ C# `try { … } catch { }`，逐处注明；
//     `subscribe` 里的 `_ = b.closeWatch(w)` 同理。
// 16) 读缓冲：Go 是 GC 管理的 `readBuf []byte`（构造函数分配、随对象回收）→ C# 构造函数里
//     `Marshal.AllocHGlobal(inotifyBufferSize)`，在 `CloseFDs`（poll 循环退出之后）显式释放：此时
//     `shutdown()` 的等待者尚未被放行，不会再有任何线程读它。
// 17) Go 的 `init()`（`inotifyWatcher.factory = …`）→ `[ModuleInitializer]`（parser/jsdoc.go 已有先例），
//     按 `OperatingSystem.IsLinux()` 守卫（C# 没有构建标签）。
// 18) 本机是 win32，本后端**无法运行验证**：以上结构体偏移表、errno 常量、O_*/IN_*/POLLIN 位值、以及
//     pipe2 的 fd 读回偏移（+0 / +4）只能静态对照 Linux 头文件（asm-generic/fcntl.h、sys/inotify.h、poll.h、
//     errno-base.h）；函数签名对照 glibc 的 sys/inotify.h / signal.h(poll) / unistd.h。
namespace TypeScript.FsWatch;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Go: <c>type inotifyBackend struct</c>。Linux inotify 后端：一个进程一个 inotify 实例，由一条长驻线程
/// （<c>Start</c>，经 <c>watcherBase.run()</c> 启动）跑 poll(2) 循环，独占事件读取与分发；所有
/// <c>subscribe</c>/<c>closeWatch</c> 在调用方线程、持 <c>_mu</c> 执行（事件循环访问 watch 表时也取 <c>_mu</c>）。
///
///	┌───────────┐        poll(2)        ┌─────────────────┐
///	│ pipe[0]   ├──────────────────────►│  start() 线程   │
///	│ (wakeup)  │                       │  (event loop)   │
///	┌───────────┐                       └────────┬────────┘
///	│ inotify   ├──────────────────────►         │
///	│ fd        │                       handleEvents()
///	└───────────┘                                ▼
///	                             subscriptions: map[wd] → []sub
///	                             每个 sub 指向自己的 dirWatch.events
///
/// 关闭：向 pipe[1] 写一个字节 → poll 在 pipe[0] 上看到 POLLIN → 循环退出 → finally 关掉 fd 并放行 endedSignal。
/// </summary>
internal sealed class InotifyBackend : WatcherBase
{
    /// <summary>Go: <c>const inotifyMask</c>（inotify_linux.go:76-86）：add_watch 订阅的事件位与 flag 位。</summary>
    private const uint _inotifyMask =
        _inCreate
        | _inDelete
        | _inDeleteSelf
        | _inModify
        | _inMoveSelf
        | _inMovedFrom
        | _inMovedTo
        | _inDontFollow
        | _inOnlyDir
        | _inExclUnlink;

    /// <summary>Go: <c>const inotifyBufferSize = 8192</c>。</summary>
    private const int _inotifyBufferSize = 8192;

    // struct pollfd / struct inotify_event 的字段偏移表（见文件头第 2 条；Linux x86-64 与 arm64 布局相同）。
    private const int _pollFdSize = 8;
    private const int _pollFdFdOffset = 0;
    private const int _pollFdEventsOffset = 4;
    private const int _pollFdReventsOffset = 6;
    private const int _inotifyEventSize = 16;
    private const int _inotifyEventFdOffset = 0; // wd
    private const int _inotifyEventMaskOffset = 4;
    private const int _inotifyEventLenOffset = 12;
    private const int _inotifyEventNameOffset = 16; // char name[]（只有 nameLen > 0 时才读）

    // sys/inotify.h 的事件位。
    private const uint _inModify = 0x0000_0002;
    private const uint _inMovedFrom = 0x0000_0040;
    private const uint _inMovedTo = 0x0000_0080;
    private const uint _inCreate = 0x0000_0100;
    private const uint _inDelete = 0x0000_0200;
    private const uint _inDeleteSelf = 0x0000_0400;
    private const uint _inMoveSelf = 0x0000_0800;
    private const uint _inQOverflow = 0x0000_4000;
    private const uint _inOnlyDir = 0x0100_0000;
    private const uint _inDontFollow = 0x0200_0000;
    private const uint _inExclUnlink = 0x0400_0000;
    private const uint _inIsDir = 0x4000_0000;

    // inotify_init1(2) 的 flags；取值等于 asm-generic/fcntl.h 的 O_* 位（Linux 上所有架构一致）。
    private const uint _inNonblock = 0x800;
    private const uint _inCloexec = 0x80000;

    // asm-generic/fcntl.h。
    private const uint _oNonblock = 0x800;
    private const uint _oCloexec = 0x80000;

    // poll.h。
    private const short _pollIn = 0x001;

    // asm-generic/errno-base.h。Linux 上 EWOULDBLOCK 与 EAGAIN 同值，故只保留一个常量。
    private const int _eintr = 4;
    private const int _eagain = 11;

    /// <summary>Go: <c>pipeFDs [2]int</c>（-1 = 未创建或已关闭）。</summary>
    private readonly int[] _pipeFDs = [-1, -1];

    /// <summary>Go: <c>pipeWriteFD atomic.Int32</c>：只经 <c>Volatile</c> / <c>Interlocked</c> 访问。</summary>
    private int _pipeWriteFD = -1;

    /// <summary>Go: <c>inotify int</c>（-1 = 未创建或已关闭）。</summary>
    private int _inotify = -1;

    /// <summary>Go: <c>subscriptions map[int][]*inotifySubscription</c>（multimap&lt;wd, sub&gt;）。</summary>
    private readonly Dictionary<int, List<InotifySubscription>> _subscriptions = [];

    /// <summary>Go: <c>endedSignal chan struct{}</c>（close = 就绪）。</summary>
    private readonly TaskCompletionSource _endedSignal = new();

    /// <summary>Go: <c>readBuf []byte</c>：跨 handleEvents 复用的读缓冲，只被 start 线程访问。</summary>
    private IntPtr _readBuf;

    /// <summary>Go: <c>watchersTouched map[*dirWatch]struct{}</c>：跨 handleEvents 复用，只被 start 线程访问。</summary>
    private readonly HashSet<DirWatch> _watchersTouched = [];

    /// <summary>Go: <c>func init() { inotifyWatcher.factory = func() watcherImpl { return newInotifyBackend() } }</c>。</summary>
    [SuppressMessage("Usage", "CA2255", Justification = "Go 的 inotify_linux.go init()：模块初始化器等价于「包被加载即注册」（见文件头第 17 条）。")]
    [ModuleInitializer]
    internal static void InitializeInotifyWatcher()
    {
        if (OperatingSystem.IsLinux())
        {
            FsWatch.InotifyWatcherInstance.Factory = static () => new InotifyBackend();
        }
    }

    internal InotifyBackend()
    {
        // Go: readBuf: make([]byte, inotifyBufferSize)（GC 内存 → 这里是非托管缓冲，见文件头第 16 条）。
        _readBuf = Marshal.AllocHGlobal(_inotifyBufferSize);
        Init(this);
    }

    /// <summary>Go: <c>func (b *inotifyBackend) start() error</c>。由 <c>WatcherBase.Run()</c> 在后台线程调用。</summary>
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
            var fd = InotifyInit1((int)(_inNonblock | _inCloexec));
            if (fd < 0)
            {
                throw Wrap("unable to initialize inotify", Marshal.GetLastPInvokeError());
            }

            _inotify = fd;

            var pollfds = Marshal.AllocHGlobal(2 * _pollFdSize);
            try
            {
                WritePollFd(pollfds, 0, _pipeFDs[0]);
                WritePollFd(pollfds, 1, _inotify);

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
    /// Go: <c>func (b *inotifyBackend) closeFDs()</c>。poll 循环退出后在 start 线程上执行；取 <c>_mu</c> 是为了让
    /// 对 <c>_inotify</c>/<c>_pipeFDs</c> 的写入与 <c>closeWatch</c>/<c>subscribe</c> 里的读取同步。
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

            if (_inotify >= 0)
            {
                _ = Close(_inotify);
                _inotify = -1;
            }

            // Go 的 readBuf 交给 GC；这里在事件循环确定退出后显式释放非托管缓冲（见文件头第 16 条）。
            if (_readBuf != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_readBuf);
                _readBuf = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Go: <c>func (b *inotifyBackend) shutdown()</c>。最后一个 watch 掉光时由 removeSharedBackend 调用：
    /// 往管道写一个字节并等 start 线程收尾。pipe write fd 用原子读，以安全对抗 start 线程的 deferred closeFDs。
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
            Marshal.WriteByte(buf, (byte)'X');
            _ = Write(fd, buf, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        _endedSignal.Task.Wait();
    }

    /// <summary>
    /// Go: <c>func (b *inotifyBackend) subscribe(w *dirWatch) error</c>。经 watcherBase 虚分派在 <c>_mu</c> 下调用
    /// （因此与 handleEvent 串行）。
    /// </summary>
    public override void Subscribe(DirWatch w)
    {
        if (!w._recursive)
        {
            try
            {
                WatchDir(w, w._dir, w._physicalDir);
            }
            catch (Exception err)
            {
                throw new DirWatchError(new IOException($"inotify_add_watch on '{w._dir}' failed: {err.Message}", err), w);
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
                    WatchDir(w, path, watchPath);
                }
                catch (Exception err)
                {
                    throw new DirWatchError(new IOException($"inotify_add_watch on '{path}' failed: {err.Message}", err), w);
                }
            });
        }
        catch
        {
            // Go: `_ = b.closeWatch(w)`（丢弃清理错误，保留遍历/订阅的原始错误）。
            try
            {
                CloseWatch(w);
            }
            catch
            {
            }

            throw;
        }
    }

    /// <summary>
    /// Go: <c>func (b *inotifyBackend) watchDir(w *dirWatch, path string, watchPath string) (int, error)</c>。
    /// 在 <paramref name="watchPath"/> 上登记内核 watch 并记录订阅；成功时返回内核 watch descriptor。
    /// </summary>
    private int WatchDir(DirWatch w, string path, string watchPath)
    {
        var wd = InotifyAddWatch(_inotify, watchPath, _inotifyMask);
        if (wd < 0)
        {
            throw ErrnoErr(Marshal.GetLastPInvokeError());
        }

        var sub = new InotifySubscription(path, watchPath, w, wd);
        if (!_subscriptions.TryGetValue(wd, out var list))
        {
            list = [];
            _subscriptions[wd] = list;
        }

        list.Add(sub);
        return wd;
    }

    /// <summary>Go: <c>func (b *inotifyBackend) handleEvents() error</c>。复用读缓冲，把缓冲区里的事件全部解析并分发。</summary>
    private void HandleEvents()
    {
        var buf = _readBuf;
        var watchersTouched = _watchersTouched;

        while (true)
        {
            var n = Read(_inotify, buf, _inotifyBufferSize);
            if (n < 0)
            {
                var errno = Marshal.GetLastPInvokeError();

                // Linux 上 EWOULDBLOCK == EAGAIN == 11，故 Go 的
                // `errors.Is(err, unix.EAGAIN) || errors.Is(err, unix.EWOULDBLOCK)` 退化为单次比较（见文件头第 12 条）。
                if (errno == _eagain)
                {
                    break;
                }

                throw Wrap("Error reading from inotify", errno);
            }

            if (n == 0)
            {
                break;
            }

            var len = (int)n;

            // 遍历缓冲区。
            for (var offset = 0; offset < len;)
            {
                var wd = Marshal.ReadInt32(buf, offset + _inotifyEventFdOffset);
                var mask = unchecked((uint)Marshal.ReadInt32(buf, offset + _inotifyEventMaskOffset));
                // +8 处的 cookie 未使用（Go 也不读）。
                var nameLen = unchecked((uint)Marshal.ReadInt32(buf, offset + _inotifyEventLenOffset));
                var recordSize = _inotifyEventSize + (int)nameLen;

                var name = "";
                if (nameLen > 0)
                {
                    // 名字 NUL 结尾（len 可能含填充）：读到首个 NUL 即止，等价 Go 的「截到第一个零字节再 string()」。
                    name = Marshal.PtrToStringUTF8(buf + offset + _inotifyEventNameOffset) ?? "";
                }

                if ((mask & _inQOverflow) != 0)
                {
                    lock (_mu)
                    {
                        foreach (var subs in _subscriptions.Values)
                        {
                            foreach (var sub in subs)
                            {
                                sub._dirWatch._events.SetError(FsWatchException.Overflow());
                                watchersTouched.Add(sub._dirWatch);
                            }
                        }
                    }

                    offset += recordSize;
                    continue;
                }

                HandleEvent(wd, mask, name, watchersTouched);
                offset += recordSize;
            }
        }

        foreach (var w in watchersTouched)
        {
            w.Notify();
        }

        watchersTouched.Clear();
    }

    /// <summary>
    /// Go: <c>func (b *inotifyBackend) handleEvent(ev *unix.InotifyEvent, name string, touched map[*dirWatch]struct{})</c>。
    /// </summary>
    private void HandleEvent(int wd, uint mask, string name, HashSet<DirWatch> touched)
    {
        lock (_mu)
        {
            // Go 的注释：b.subscriptions[wd] 里每个 *inotifySubscription 指针最多出现一次（watchDir 总是 append
            // 新结构体），所以不需要去重；上游 C++ 用 shared_ptr 身份做键的 unordered_set，这里的等价不变量是结构性的。
            if (!_subscriptions.TryGetValue(wd, out var list))
            {
                return; // Go: range 一个 nil 切片 = 零次迭代。
            }

            // 按**下标**遍历同一个 List 实例：删除分支会原地压缩它（见文件头第 9 条）。先取出 s 再调用，
            // 等价 Go 的 range 变量拷贝。
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (HandleSubscription(mask, name, s))
                {
                    touched.Add(s._dirWatch);
                }
            }
        }
    }

    /// <summary>
    /// Go: <c>func (b *inotifyBackend) handleSubscription(ev *unix.InotifyEvent, name string, sub *inotifySubscription) bool</c>。
    /// 返回 false 表示这个订阅没有产生事件（只有「非 watch 根的 self 事件」这一条路径）。
    /// </summary>
    private bool HandleSubscription(uint mask, string name, InotifySubscription sub)
    {
        var w = sub._dirWatch;
        var path = sub._path;
        var watchPath = sub._watchPath;
        var isDir = (mask & _inIsDir) != 0;
        if (name.Length != 0)
        {
            path = path + "/" + name;
            watchPath = watchPath + "/" + name;
        }

        if ((mask & (_inCreate | _inMovedTo)) != 0)
        {
            w._events.Create(path);
            if (isDir && w._recursive)
            {
                // Go: `_ = walkDir(...)`，遍历里对新目录 `_, _ = b.watchDir(...)`（失败被丢弃，不打断遍历与分发）。
                try
                {
                    FsWatch.WalkDir(watchPath, true, (p, pIsDir) =>
                    {
                        if (!pIsDir)
                        {
                            return;
                        }

                        try
                        {
                            WatchDir(w, w.DisplayPath(p), p);
                        }
                        catch
                        {
                            // Go: `_, _ = b.watchDir(w, w.displayPath(p), p)`。
                        }
                    });
                }
                catch
                {
                    // Go: `_ = walkDir(watchPath, true, …)`。
                }
            }
        }
        else if ((mask & _inModify) != 0)
        {
            w._events.Update(path);
        }
        else if ((mask & (_inDelete | _inDeleteSelf | _inMovedFrom | _inMoveSelf)) != 0)
        {
            var isSelfEvent = (mask & (_inDeleteSelf | _inMoveSelf)) != 0;

            // 除非这就是 watch 根，否则忽略 delete/move self 事件（不产生回调）。
            if (isSelfEvent && path != w._dir)
            {
                return false;
            }

            // 被删的是目录时，连同其后代一起摘掉订阅。
            // XXX: self 事件不带 IN_ISDIR（Go 原注释）。
            if (isSelfEvent || isDir)
            {
                // Go 在 range map 的同时 delete 是合法的；C# 需要键快照（见文件头第 10 条）。
                foreach (var wd in new List<int>(_subscriptions.Keys))
                {
                    if (!_subscriptions.TryGetValue(wd, out var list))
                    {
                        continue;
                    }

                    list.RemoveAll(s => IsPathOrDescendant(s._path, path));
                    if (list.Count == 0)
                    {
                        _ = InotifyRmWatch(_inotify, wd);
                        _subscriptions.Remove(wd);
                    }
                }
            }

            w._events.Remove(path);

            // watch 根自己没了：内核已自动移除该 dirWatch 的全部 wd，不会再有事件。用 ErrWatchTerminated 让
            // 调用方知道要清理；上面的 delete 事件仍会经同一条回调送出。
            if (isSelfEvent && path == w._dir)
            {
                w._events.SetError(FsWatchException.WatchTerminated("fswatch: watch terminated: watched directory removed"));
            }
        }

        return true;
    }

    /// <summary>
    /// Go 在 handleSubscription 里内联的判定：<c>s.path == path || (len(s.path) &gt; len(path) &amp;&amp;
    /// s.path[len(path)] == '/' &amp;&amp; s.path[:len(path)] == path)</c>，即 path 本身或其后代。
    /// **不复用** <c>FsWatch.IsInDirectoryOrSelf</c>：那条 helper 对以分隔符结尾的 dir（例如 "/"）语义不同（见文件头第 14 条）。
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

    /// <summary>
    /// Go: <c>func (b *inotifyBackend) closeWatch(w *dirWatch) error</c>。遍历所有引用了 w 的 wd 摘掉对应订阅；
    /// 某个 wd 空了就 InotifyRmWatch。InotifyRmWatch 失败时**继续处理剩余 wd**、只返回第一个错误——提前返回会把
    /// 内部状态清一半，让调用方的 dirWatch 还挂在别的 wd 上。
    /// </summary>
    public override void CloseWatch(DirWatch w)
    {
        Exception? firstErr = null;

        foreach (var wd in new List<int>(_subscriptions.Keys))
        {
            if (!_subscriptions.TryGetValue(wd, out var list))
            {
                continue;
            }

            if (list.RemoveAll(s => ReferenceEquals(s._dirWatch, w)) == 0)
            {
                continue;
            }

            if (list.Count == 0)
            {
                if (InotifyRmWatch(_inotify, wd) != 0)
                {
                    firstErr ??= new DirWatchError(Wrap("unable to remove dirWatch", Marshal.GetLastPInvokeError()), w);
                }

                _subscriptions.Remove(wd);
            }
        }

        if (firstErr is not null)
        {
            throw firstErr;
        }
    }

    // ---- 非托管布局与 errno 辅助 -------------------------------------------------

    /// <summary>把第 <paramref name="index"/> 个 pollfd 写成 { fd, POLLIN, revents = 0 }。</summary>
    private static void WritePollFd(IntPtr pollfds, int index, int fd)
    {
        var baseOffset = index * _pollFdSize;
        Marshal.WriteInt32(pollfds, baseOffset + _pollFdFdOffset, fd);
        Marshal.WriteInt16(pollfds, baseOffset + _pollFdEventsOffset, _pollIn);
        Marshal.WriteInt16(pollfds, baseOffset + _pollFdReventsOffset, 0);
    }

    private static short ReadRevents(IntPtr pollfds, int index) =>
        Marshal.ReadInt16(pollfds, (index * _pollFdSize) + _pollFdReventsOffset);

    /// <summary>Go 的裸 errno 错误（<c>errno.Errno</c>：只有 strerror 文本，没有前缀）。</summary>
    private static IOException ErrnoErr(int errno) => new(ErrnoText(errno), new Win32Exception(errno));

    /// <summary>Go 的 <c>fmt.Errorf("…: %w", err)</c>：消息拼接 + errno 放进 InnerException（见文件头第 13 条）。</summary>
    private static IOException Wrap(string message, int errno) =>
        new($"{message}: {ErrnoText(errno)}", new Win32Exception(errno));

    private static string ErrnoText(int errno) => new Win32Exception(errno).Message;

    // ---- libc（Linux glibc；全部 SetLastError = true）---------------------------

    [DllImport("libc", EntryPoint = "pipe2", SetLastError = true)]
    private static extern int Pipe2(IntPtr pipefd, int flags);

    [DllImport("libc", EntryPoint = "inotify_init1", SetLastError = true)]
    private static extern int InotifyInit1(int flags);

    [DllImport("libc", EntryPoint = "inotify_add_watch", SetLastError = true)]
    private static extern int InotifyAddWatch(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, uint mask);

    [DllImport("libc", EntryPoint = "inotify_rm_watch", SetLastError = true)]
    private static extern int InotifyRmWatch(int fd, int wd);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int Poll(IntPtr fds, nuint nfds, int timeout);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint Read(int fd, IntPtr buf, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint Write(int fd, IntPtr buf, nuint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
}

/// <summary>Go: <c>type inotifySubscription struct</c>。一个 wd 上的一次订阅（同一 wd 可被多个订阅共用）。</summary>
internal sealed class InotifySubscription
{
    internal string _path;
    internal string _watchPath;
    internal DirWatch _dirWatch;

    /// <summary>内核 watch descriptor；Go 里与 map 键重复且从未被读过，为结构 1:1 保留。</summary>
    internal int _wd;

    internal InotifySubscription(string path, string watchPath, DirWatch dirWatch, int wd)
    {
        _path = path;
        _watchPath = watchPath;
        _dirWatch = dirWatch;
        _wd = wd;
    }
}