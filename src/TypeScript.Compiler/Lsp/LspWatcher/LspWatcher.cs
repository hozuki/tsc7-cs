// ported from: internal/lsp/lspwatcher/lspwatcher.go:1-597
//
// 与 Go 的偏离（逐条）：
// 1) 包级常量/函数（`throttleWindow`、`New`、`NewWithFSWatcher`、`newWithBackend`、`watchRoot`、`rootFromGlob`、
//    `watchPatternString`、`isRecursiveGlob`、`effectiveKind`、`nearestExistingAncestor`）→ 与 package 同名的静态类
//    `LspWatcher` 的成员（docs/architecture.md §2）；调用点用 `using static TypeScript.LspWatcher.LspWatcher;`
//    裸名调用（`New`/`NewWithFSWatcher` 保持 Go 的原名）。
// 2) 未导出接口 `watcherBackend` → `WatcherBackend`，**不加 `I` 前缀**：本仓对 Go 接口一律保留 PascalCase 原名，
//    见 ContentMapper/HostImpl.cs 的 `Spawner`、Ls/CrossProject.cs 的 `CrossProjectOrchestrator`、
//    Project/Client.cs 的 `Client`（`IProcessExitState` 是仅有的例外，且那里是为了避开同名类型）。
//    `defaultWatcherBackend` → `DefaultWatcherBackend`（sealed class，Go 的 `struct` 无指针接收者差异）。
// 3) `io.Closer` → `TypeScript.FsWatch.Watch`：本仓 FsWatch 已把 Go 的 `fswatch.Watch`（唯一方法 `Close()`）移植成
//    `Watch`，它就是本仓"可关闭监听句柄"的类型；再包一层 `IDisposable` 反而要新造没有对应物的包装类型。
//    因此 `WatcherBackend.WatchDirectory` 与 `watch.subscription` 都用 `TypeScript.FsWatch.Watch`。
//    因 `Watcher`/`Watch` 在本命名空间里与 `TypeScript.FsWatch` 的同名类型冲突，文件内用别名 `FsWatchWatcher`/
//    `FsWatchSubscription` 指向 FsWatch 的那两个类型（本仓既有做法：ContentMapper/HostImpl.cs 的 `Path`/`Loc`）。
// 4) `error` → 异常：`WatchFiles`/`UnwatchFiles` 改为抛 `InvalidOperationException`（消息文本逐字照 Go 的
//    `errors.New`/`fmt.Errorf`，含 `%q` 的双引号），`watch.reconcile` 改为 `void` + 安装失败即抛。
//    Go 里**被显式丢弃**的 error（`_ = w.reconcile(...)`、`_ = w.UnwatchFiles(id)`、`_ = vfs.WalkDir(...)`）用
//    `try { … } catch (Exception) { }` 复刻"丢弃"语义：直接让它抛会改变可观察行为（Go 不会因这些失败打断
//    fswatch 的投递线程）。`_ = previous.Close()` 不额外捕获 —— C# 的 `Watch.Close()` 是 `void`，调用点与 Go 一样
//    不接收结果，失败让它抛（同 FsWatch/Watcher.cs 文件头第 9 条）。
// 5) `time.AfterFunc(throttleWindow, w.flush)` → `System.Threading.Timer`（`ScheduleFlushLocked` 里一次性到期，
//    period = `Timeout.InfiniteTimeSpan`）。Go 的定时器由 runtime 回收，C# 的要在 `Flush` 里置空并 `Dispose()`。
//    `flushTimer *time.Timer` → `Timer?`。
// 6) `(T, bool)` → `TryX(out T)`：`watchRoot` → `TryWatchRoot`、`nearestExistingAncestor` →
//    `TryNearestExistingAncestor`。`*T` → `T?`（`FileSystemWatcher.Kind`、`GlobPattern.Pattern`、`BaseUri.URI`
//    在生成物里本就是可空语义）。
// 7) `[]T` 的 nil→零次迭代：`fileSystemWatchers ?? []`、`entries.Files ?? []` / `entries.Directories ?? []`；
//    `forwardEvents` 的 `events` 在唯一调用点已被 `Length > 0` 挡过，故声明为非空数组。
//    `make(map[string]*lsproto.FileEvent, n)` → `new Dictionary<string, FileEvent>(n)`（capacity 只是提示）。
//    `emitSyntheticCreates`/`enqueueSyntheticCreates` 里的 `[]string` 用 `List<string>`（Go 侧就是往切片里 append，
//    C# 里只有 `List<T>` 能原地追加；`enqueueSyntheticCreates` 的形参因此从 `paths []string` 变成 `List<string>`）。
//    Go 的 map 迭代顺序随机、C# `Dictionary` 是插入序，`flush` 交给 `onChanges` 的批次顺序因此在两边不可比
//    （Go 侧本身也不保证顺序）。
// 8) `logging.Logger.Logf(format, args...)` 的格式串在每个调用点改写成 C# 复合格式（`%q` → `"{0}"`、
//    `%v` → `{0}`，见 Project/Logging/Logger.cs 文件头第 4 条）；`%v` 作用于 Go 的 error 时取的是 `err.Error()`，
//    C# 侧对应 `Exception.Message`。
// 9) `sync.Mutex` → `lock (_mu)`；Go 里"调用方已持锁"的 `scheduleFlushLocked` 保持同名 `ScheduleFlushLocked` 且
//    自身不加锁（调用点都写在 `lock` 块内）。Go 的 `defer w.mu.Unlock()` → `reconcile` 的整个循环（含 backend
//    调用）都在 `lock` 块内，与 Go 一致。Go 在释放锁之后才调 `close()` 的地方（`Close`/`UnwatchFiles`/
//    `WatchFiles` 的 closed 分支）用局部变量先出锁再调用。
// 10) `Watcher.watches map[string][]*watch` → `Dictionary<string, List<Watch>>`；`Close` 里的 `watches = nil`
//     → `_watches = null`，`UnwatchFiles` 对 nil map 的行为（Go 里取值为零值 + `ok=false`）用 `_watches is null`
//     显式表达。Go 的 `w.watches[id] = nil`（先占位再 append）→ `_watches[id] = []`。
using System.Collections.Generic;
using System.Threading;
using TypeScript.FsWatch;
using TypeScript.LsProto;
using TypeScript.Project.Logging;
using TypeScript.Vfs;
using static TypeScript.FsWatch.FsWatch;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LspWatcher.LspWatcher;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.Vfs;
// `System.IO.FileSystemWatcher`（隐式 using System.IO）与 `TypeScript.LsProto.FileSystemWatcher` 同名 → CS0104，必须别名。
using FileSystemWatcher = TypeScript.LsProto.FileSystemWatcher;
using FsWatchSubscription = TypeScript.FsWatch.Watch;
using FsWatchWatcher = TypeScript.FsWatch.Watcher;

namespace TypeScript.LspWatcher;

/// <summary>
/// Go: <c>type watcherBackend interface</c>。安装一次目录订阅的后端；把它抽出来是为了让 <see cref="Watcher"/>
/// 能在测试里换成假后端（Go 侧 <c>defaultWatcherBackend</c> 只是 <c>fswatch.Watcher</c> 的转发）。
/// </summary>
internal interface WatcherBackend
{
    /// <summary>Go: <c>WatchDirectory(dir string, fn fswatch.WatchCallback, opts ...fswatch.WatchOption) (io.Closer, error)</c>。</summary>
    FsWatchSubscription WatchDirectory(string dir, WatchCallback fn, params WatchOption[] opts);
}

/// <summary>Go: <c>type defaultWatcherBackend struct</c>。把订阅转发给真实的 <c>fswatch.Watcher</c>。</summary>
internal sealed class DefaultWatcherBackend(FsWatchWatcher watcher) : WatcherBackend
{
    private readonly FsWatchWatcher _watcher = watcher;

    /// <summary>Go: <c>func (d defaultWatcherBackend) WatchDirectory(...)</c>。</summary>
    public FsWatchSubscription WatchDirectory(string dir, WatchCallback fn, params WatchOption[] opts) =>
        _watcher.WatchDirectory(dir, fn, opts);
}

/// <summary>
/// Go: <c>type Watcher struct</c>。按 LSP WatcherID 字符串管理一组文件系统订阅（与 LSP server 的
/// <c>project.WatcherID</c> 对应）。事件以批次形式投给 <c>onChanges</c>，形状与
/// <c>workspace/didChangeWatchedFiles</c> 通知一致。
/// </summary>
public sealed class Watcher
{
    internal readonly FS _fs;
    internal readonly WatcherBackend _backend;
    internal readonly Action<FileEvent[]> _onChanges;
    internal readonly Logger _logger;

    private readonly object _mu = new();

    /// <summary>Go: <c>watches map[string][]*watch</c>。一个 id 可能对应多个 watch（注册里每个
    /// FileSystemWatcher 各自成一个 watch，根与 kind 可以不同）。</summary>
    private Dictionary<string, List<Watch>>? _watches = [];

    private bool _closed;

    // 待发批次状态，全部由 _mu 保护。
    private Dictionary<string, FileEvent>? _pending;
    private Timer? _flushTimer;

    internal Watcher(FS fs, WatcherBackend backend, Action<FileEvent[]> onChanges, Logger logger)
    {
        _fs = fs;
        _backend = backend;
        _onChanges = onChanges;
        _logger = logger;
    }

    /// <summary>
    /// Go: <c>func (w *Watcher) WatchFiles(id string, fileSystemWatchers []*lsproto.FileSystemWatcher) error</c>。
    ///
    /// 目录尚不存在不算错误：会在最近的已存在祖先进上装一个祖先订阅并报告成功，让会话对"这个 watcher 活着"
    /// 的判断在整个生命周期内都成立。只有真正的后端失败（例如监听已存在目录时资源耗尽）才会回滚整个 id 并
    /// 抛异常，好让会话的 pending/retry 路径在下一次重新求值时重新注册。
    /// </summary>
    public void WatchFiles(string id, FileSystemWatcher[]? fileSystemWatchers)
    {
        lock (_mu)
        {
            if (_closed)
            {
                throw new InvalidOperationException("lspwatcher: closed");
            }

            if (_watches!.ContainsKey(id))
            {
                throw new InvalidOperationException($"lspwatcher: watcher \"{id}\" already exists");
            }

            // 先把这个 id 标记为"已存在"，这样并发的同 id WatchFiles 会在上面被拒。
            _watches[id] = [];
        }

        var failed = false;
        foreach (var fileSystemWatcher in fileSystemWatchers ?? [])
        {
            if (!TryWatchRoot(fileSystemWatcher, out var directory) || directory == "")
            {
                _logger.Logf(
                    "lspwatcher: skipping watcher \"{0}\": unrecognized pattern \"{1}\"",
                    id,
                    WatchPatternString(fileSystemWatcher));
                continue;
            }

            var newWatch = new Watch
            {
                _watcher = this,
                _requestedDirectory = directory,
                _kind = EffectiveKind(fileSystemWatcher),
                _recursive = IsRecursiveGlob(fileSystemWatcher),
            };

            try
            {
                newWatch.Reconcile(false /*emitSynthetic*/);
            }
            catch (Exception ex)
            {
                _logger.Logf("lspwatcher: failed to register watcher \"{0}\" for \"{1}\": {2}", id, directory, ex.Message);
                newWatch.Close();
                failed = true;
                break;
            }

            bool closedNow;
            lock (_mu)
            {
                closedNow = _closed;
                if (!closedNow)
                {
                    _watches![id].Add(newWatch);
                }
            }

            if (closedNow)
            {
                newWatch.Close();
                throw new InvalidOperationException("lspwatcher: closed");
            }
        }

        if (failed)
        {
            // 回滚整个 id，好让会话的重试（MarkPending）能干净地重新注册它 —— 会话把 id 当作一个整体。
            try
            {
                UnwatchFiles(id);
            }
            catch (InvalidOperationException)
            {
                // Go: _ = w.UnwatchFiles(id)（丢弃该 error）
            }

            throw new InvalidOperationException($"lspwatcher: failed to register one or more watchers for \"{id}\"");
        }
    }

    /// <summary>Go: <c>func (w *Watcher) UnwatchFiles(id string) error</c>。拆掉 id 关联的全部订阅。</summary>
    public void UnwatchFiles(string id)
    {
        List<Watch> watches;
        lock (_mu)
        {
            if (_watches is null || !_watches.TryGetValue(id, out var found))
            {
                throw new InvalidOperationException($"lspwatcher: no watcher with id \"{id}\"");
            }

            watches = found;
            _watches.Remove(id);
        }

        foreach (var watch in watches)
        {
            watch.Close();
        }
    }

    /// <summary>Go: <c>func (w *Watcher) Close()</c>。移除全部订阅；可重复调用。</summary>
    public void Close()
    {
        Dictionary<string, List<Watch>>? watchesByID;
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            watchesByID = _watches;
            _watches = null;
            if (_flushTimer is not null)
            {
                _flushTimer.Dispose();
                _flushTimer = null;
            }

            _pending = null;
        }

        if (watchesByID is null)
        {
            return;
        }

        foreach (var watches in watchesByID.Values)
        {
            foreach (var watch in watches)
            {
                watch.Close();
            }
        }
    }

    /// <summary>
    /// Go: <c>func (w *Watcher) forwardEvents(kind lsproto.WatchKind, events []fswatch.Event)</c>。
    /// 把 fswatch 事件翻译成 LSP 文件事件并入队，等下一次防抖 flush 投递。
    /// </summary>
    internal void ForwardEvents(WatchKind kind, Event[] events)
    {
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }

            _pending ??= new Dictionary<string, FileEvent>(events.Length);
            foreach (var @event in events)
            {
                FileChangeType changeType;
                switch (@event.Kind)
                {
                    case EventKind.Update:
                        // fswatch 有意不区分 create 与 update。对 LSP 消费方无妨：调用方从自己的缓存推断
                        // create/update，两者都该让陈旧状态失效。
                        if ((kind & (WatchKind.Create | WatchKind.Change)) == 0)
                        {
                            continue;
                        }

                        changeType = FileChangeType.Changed;
                        break;

                    case EventKind.Delete:
                        if ((kind & WatchKind.Delete) == 0)
                        {
                            continue;
                        }

                        changeType = FileChangeType.Deleted;
                        break;

                    default:
                        continue;
                }

                var path = NormalizeSlashes(@event.Path);
                var uri = FileNameToDocumentURI(path);
                _pending[uri.Value] = new FileEvent { Uri = uri, Type = changeType };
            }

            ScheduleFlushLocked();
        }
    }

    /// <summary>
    /// Go: <c>func (w *Watcher) emitSyntheticCreates(directory string, kind lsproto.WatchKind, recursive bool)</c>。
    /// 目标订阅在 missing→present 之后被（重新）装上时补齐合成 create 事件，好让会话重新解析在真订阅存在之前
    /// 出现的文件。目标目录本身一定包含；非递归 watch 加它的直接子项，递归 watch 走整棵子树。watch 不要求
    /// create 通知时什么都不发。
    /// </summary>
    internal void EmitSyntheticCreates(string directory, WatchKind kind, bool recursive)
    {
        if ((kind & WatchKind.Create) == 0)
        {
            return;
        }

        var paths = new List<string> { directory };
        if (recursive)
        {
            try
            {
                WalkDir(_fs, directory, (path, entry, err) =>
                {
                    if (err is null && path != directory)
                    {
                        paths.Add(path);
                    }

                    return WalkDirResult.Ok;
                });
            }
            catch (Exception)
            {
                // Go: _ = vfs.WalkDir(...)（丢弃该 error）
            }
        }
        else
        {
            var entries = _fs.GetAccessibleEntries(directory);
            foreach (var name in entries.Files ?? [])
            {
                paths.Add(CombinePaths(directory, name));
            }

            foreach (var name in entries.Directories ?? [])
            {
                paths.Add(CombinePaths(directory, name));
            }
        }

        EnqueueSyntheticCreates(paths);
    }

    /// <summary>
    /// Go: <c>func (w *Watcher) enqueueSyntheticCreates(paths []string)</c>。为 paths 补合成 create 事件，但不覆盖
    /// 同一路径上已经待发的更具体事件（例如真实的 delete）。
    /// </summary>
    private void EnqueueSyntheticCreates(List<string> paths)
    {
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }

            _pending ??= new Dictionary<string, FileEvent>(paths.Count);
            foreach (var path in paths)
            {
                var uri = FileNameToDocumentURI(path);
                if (_pending.ContainsKey(uri.Value))
                {
                    continue;
                }

                _pending[uri.Value] = new FileEvent { Uri = uri, Type = FileChangeType.Created };
            }

            ScheduleFlushLocked();
        }
    }

    /// <summary>
    /// Go: <c>func (w *Watcher) scheduleFlushLocked()</c>。防抖 flush 定时器尚未挂起时挂起它。
    /// 调用方必须持有 <c>_mu</c>（Go 的 <c>w.mu</c>）。
    /// </summary>
    private void ScheduleFlushLocked()
    {
        _flushTimer ??= new Timer(
            static state => ((Watcher)state!).Flush(),
            this,
            ThrottleWindow,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>Go: <c>func (w *Watcher) flush()</c>。投递当前批次（无事件时直接返回）。</summary>
    private void Flush()
    {
        Dictionary<string, FileEvent>? pending;
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }

            pending = _pending;
            _pending = null;
            var timer = _flushTimer;
            _flushTimer = null;
            timer?.Dispose();
        }

        if (pending is null || pending.Count == 0)
        {
            return;
        }

        var changes = new FileEvent[pending.Count];
        var index = 0;
        foreach (var @event in pending.Values)
        {
            changes[index++] = @event;
        }

        _onChanges(changes);
    }
}

/// <summary>
/// Go: <c>type watch struct</c>。代表 LSP 注册里的一个 FileSystemWatcher。
///
/// 会话要求监听的目录可能还不存在（granular 模式下很常见：每个探测到但不存在的包目录都会成为一个 watch），
/// 也可能在监听期间被删除。为了跨越这些状态，watch 始终维持二者之一：
/// <list type="bullet">
/// <item>目标订阅：直接根在请求的目录上（一旦它存在）；或</item>
/// <item>祖先订阅：挂在最近的已存在祖先进上（非递归），用来发现请求的目录 —— 或某个中间路径组件 —— 被创建，
/// 之后 watch 会逐级下降并最终提升为目标订阅。</item>
/// </list>
///
/// 目标成真时会为它发出合成 create 事件（并视 watch 是否递归，为它的直接子项或整棵子树发出），好让会话重新
/// 解析在真订阅装上之前的空档里出现的文件。
///
/// 所有路径字段都是 tspath 风格的（正斜杠）绝对路径。
/// </summary>
internal sealed class Watch
{
    internal Watcher _watcher = null!;

    /// <summary>Go: <c>requestedDirectory</c>。LSP 层请求的目录（可能是符号链接）。</summary>
    internal string _requestedDirectory = "";

    internal WatchKind _kind;

    /// <summary>Go: <c>recursive</c>。目标订阅是否应该递归。</summary>
    internal bool _recursive;

    internal readonly object _mu = new();

    /// <summary>Go: <c>subscription io.Closer</c>。当前订阅（目标或祖先）；没有时为 null。</summary>
    internal FsWatchSubscription? _subscription;

    /// <summary>Go: <c>watchedDirectory</c>。<c>_subscription</c> 所根的目录（规范化后）。</summary>
    internal string _watchedDirectory = "";

    /// <summary>Go: <c>watchingTarget</c>。<c>_subscription</c> 是否根在目标目录上。</summary>
    internal bool _watchingTarget;

    internal bool _closed;

    /// <summary>
    /// Go: <c>func (w *watch) close()</c>。拆掉 watch 当前的订阅，并阻止任何在途的 reconcile 重新装上它。
    /// </summary>
    internal void Close()
    {
        FsWatchSubscription? subscription;
        lock (_mu)
        {
            _closed = true;
            subscription = _subscription;
            _subscription = null;
            _watchedDirectory = "";
        }

        subscription?.Close();
    }

    /// <summary>
    /// Go: <c>func (w *watch) reconcile(emitSyntheticCreates bool) error</c>。按当前文件系统状态安装或推进本 watch
    /// 走向目标目录。注册时、祖先 watch 观察到活动时、以及目标 watch 因删除而终止之后都会被调用。
    ///
    /// <paramref name="emitSyntheticCreates"/> 控制"提升到目标"时是否发合成 create 事件：目标已存在时的首次安装
    /// 传 false（会话已经知道那些文件），任何 missing→present 的恢复都传 true。
    ///
    /// 只有真正安装订阅失败才会抛（Go 的返回非 nil error）；目标目录缺失会走祖先订阅并正常返回（Go 的 nil）。
    /// </summary>
    internal void Reconcile(bool emitSyntheticCreates)
    {
        lock (_mu)
        {
            var watcher = _watcher;
            while (true)
            {
                if (_closed)
                {
                    return;
                }

                if (watcher._fs.DirectoryExists(_requestedDirectory))
                {
                    if (_watchingTarget && _subscription is not null)
                    {
                        return; // 已经在监听目标
                    }

                    var targetDirectory = _requestedDirectory;
                    WatchOption[] options = _recursive ? [WithRecursive()] : [];
                    var subscription = watcher._backend.WatchDirectory(targetDirectory, TargetCallback(targetDirectory), options);
                    var previous = _subscription;
                    _subscription = subscription;
                    _watchedDirectory = targetDirectory;
                    _watchingTarget = true;
                    previous?.Close();
                    if (emitSyntheticCreates)
                    {
                        watcher.EmitSyntheticCreates(targetDirectory, _kind, _recursive);
                    }

                    return;
                }

                if (!TryNearestExistingAncestor(watcher._fs, _requestedDirectory, out var ancestor))
                {
                    // 没有任何东西可监听（连根都没了）；丢掉已有订阅。
                    if (_subscription is not null)
                    {
                        var previous = _subscription;
                        _subscription = null;
                        _watchedDirectory = "";
                        _watchingTarget = false;
                        previous.Close();
                    }

                    return;
                }

                var ancestorDirectory = ancestor;
                if (!_watchingTarget && _subscription is not null && _watchedDirectory == ancestorDirectory)
                {
                    return; // 已经在监听正确的祖先进
                }

                var ancestorSubscription = watcher._backend.WatchDirectory(ancestorDirectory, AncestorCallback());
                var previousSubscription = _subscription;
                _subscription = ancestorSubscription;
                _watchedDirectory = ancestorDirectory;
                _watchingTarget = false;
                previousSubscription?.Close();

                // 目标可能在上面的 DirectoryExists 检查与这次祖先订阅安装之间出现（例如原子地整棵树一起创建），
                // 所以继续循环以进一步下降或立即提升。从这里开始的任何提升都是 missing→present，故合成 create。
                emitSyntheticCreates = true;
            }
        }
    }

    /// <summary>
    /// Go: <c>func (w *watch) targetCallback(watchedDirectory string) fswatch.WatchCallback</c>。目标 watch（根在
    /// <paramref name="watchedDirectory"/>）的 fswatch 回调：把事件转给会话，并在 <c>ErrWatchTerminated</c>
    /// （被监听目录被删）时退回到最近的已存在祖先进，好在目录重建时重新挂上。
    /// </summary>
    private WatchCallback TargetCallback(string watchedDirectory)
    {
        var watcher = _watcher;
        return (events, err) =>
        {
            var terminated = false;
            if (err is not null)
            {
                if (FsWatchException.Is(err, FsWatchErrorKind.Overflow))
                {
                    watcher._logger.Logf(
                        "lspwatcher: watch overflow in \"{0}\" (some events may have been dropped): {1}",
                        watchedDirectory,
                        err.Message);
                }
                else if (FsWatchException.Is(err, FsWatchErrorKind.WatchTerminated))
                {
                    terminated = true;
                    watcher._logger.Logf(
                        "lspwatcher: watch terminated in \"{0}\" (directory removed): {1}",
                        watchedDirectory,
                        err.Message);
                }
                else
                {
                    watcher._logger.Logf("lspwatcher: watch error in \"{0}\": {1}", watchedDirectory, err.Message);
                }
            }

            if (events is { Length: > 0 })
            {
                watcher.ForwardEvents(_kind, events);
            }

            if (terminated)
            {
                // 目录的 delete 事件已在上面转发；现在重新挂到最近的已存在祖先进上。
                HandleTerminated();
            }
        };
    }

    /// <summary>
    /// Go: <c>func (w *watch) handleTerminated()</c>。清掉已死掉的目标 watch（后端已经移除它）并重新求值，退回到
    /// 最近已存在祖先进上的祖先 watch，好在目录重新出现时再挂上。先清状态是必要的：否则 reconcile 会看到
    /// <c>watchingTarget &amp;&amp; subscription != nil</c> 并认定目标已在监听，尽管订阅已经死了 —— 一旦目录在
    /// reconcile 之前被重建，就丢了恢复能力。
    /// </summary>
    private void HandleTerminated()
    {
        FsWatchSubscription? previous;
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }

            previous = _subscription;
            _subscription = null;
            _watchedDirectory = "";
            _watchingTarget = false;
        }

        previous?.Close();

        try
        {
            Reconcile(true /*emitSyntheticCreates*/);
        }
        catch (Exception)
        {
            // Go: _ = w.reconcile(true)（丢弃该 error）
        }
    }

    /// <summary>
    /// Go: <c>func (w *watch) ancestorCallback() fswatch.WatchCallback</c>。祖先 watch 的 fswatch 回调。祖先 watch
    /// 只为发现目标 —— 或某个中间路径组件 —— 被创建而存在；它们的事件讲的是会话并不跟踪的祖先目录，所以事件
    /// 被忽略，只把 watch 重新求值一次。
    /// </summary>
    private WatchCallback AncestorCallback()
    {
        return (events, err) =>
        {
            try
            {
                Reconcile(true /*emitSyntheticCreates*/);
            }
            catch (Exception)
            {
                // Go: _ = w.reconcile(true)（丢弃该 error）
            }
        };
    }
}

/// <summary>
/// Go: <c>package lspwatcher</c> 的包级常量、构造函数与文件系统/glob 辅助函数（docs/architecture.md §2）。
/// </summary>
public static partial class LspWatcher
{
    /// <summary>
    /// Go: <c>const throttleWindow = 75 * time.Millisecond</c>。对齐 VS Code 的 parcel watcher 集成：给首批事件一个
    /// 短暂的宽限窗口，让相邻的文件系统突发合并。
    /// </summary>
    internal static readonly TimeSpan ThrottleWindow = TimeSpan.FromMilliseconds(75);

    /// <summary>Go: <c>func New(fs vfs.FS, onChanges func(changes []*lsproto.FileEvent), logger logging.Logger) *Watcher</c>。
    /// 用 internal/fswatch 的平台默认 watcher 实现构造 <see cref="Watcher"/>。</summary>
    public static Watcher New(FS fs, Action<FileEvent[]> onChanges, Logger logger) =>
        NewWithFSWatcher(fs, Default(), onChanges, logger);

    /// <summary>
    /// Go: <c>func NewWithFSWatcher(fs vfs.FS, watcher fswatch.Watcher, onChanges func(changes []*lsproto.FileEvent), logger logging.Logger) *Watcher</c>。
    /// 用给定的 fswatch.Watcher 构造 <see cref="Watcher"/>；用它选定具体后端（例如 <c>fswatch.Kqueue()</c>）而不是
    /// 平台默认。
    /// </summary>
    public static Watcher NewWithFSWatcher(FS fs, FsWatchWatcher watcher, Action<FileEvent[]> onChanges, Logger logger) =>
        NewWithBackend(fs, new DefaultWatcherBackend(watcher), onChanges, logger);

    /// <summary>Go: <c>func newWithBackend(fs vfs.FS, backend watcherBackend, onChanges func(changes []*lsproto.FileEvent), logger logging.Logger) *Watcher</c>。</summary>
    internal static Watcher NewWithBackend(FS fs, WatcherBackend backend, Action<FileEvent[]> onChanges, Logger logger) =>
        new(fs, backend, onChanges, logger);

    /// <summary>
    /// Go: <c>func watchRoot(fileSystemWatcher *lsproto.FileSystemWatcher) (string, bool)</c>。从 FileSystemWatcher 里
    /// 取出 fswatch 订阅应该根的目录。项目层产生的模式形如 <c>&lt;dir&gt;/**/*</c>（递归）或 <c>&lt;dir&gt;/*</c>
    /// （非递归，granular watch 模式用），要么是 Pattern 形态的完全限定目录，要么是带 <c>file://</c> BaseUri 的
    /// RelativePattern，所以"第一个 glob 元字符之前的一切"这个启发式是可靠的。用
    /// <see cref="IsRecursiveGlob"/> 判断订阅是否应该递归。
    ///
    /// 返回的根是 tspath 规范化的（正斜杠）绝对路径。
    /// </summary>
    internal static bool TryWatchRoot(FileSystemWatcher fileSystemWatcher, out string root)
    {
        if (fileSystemWatcher.GlobPattern.Pattern is { } pattern)
        {
            root = RootFromGlob(pattern);
            return true;
        }

        if (fileSystemWatcher.GlobPattern.RelativePattern is { } relativePattern)
        {
            if (relativePattern.BaseUri.URI is not { } uri)
            {
                root = "";
                return false;
            }

            var baseUri = new DocumentUri(uri.Value).FileName();
            var combined = CombinePaths(baseUri, relativePattern.Pattern);
            root = RootFromGlob(combined);
            return true;
        }

        root = "";
        return false;
    }

    /// <summary>Go: <c>func rootFromGlob(pattern string) string</c>。取 glob 模式里第一个元字符之前的目录。</summary>
    internal static string RootFromGlob(string pattern)
    {
        pattern = NormalizeSlashes(pattern);
        var metaIndex = -1;
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '*' or '?' or '[' or '{':
                    metaIndex = i;
                    break;
            }

            if (metaIndex != -1)
            {
                break;
            }
        }

        if (metaIndex == -1)
        {
            return NormalizePath(pattern.TrimEnd('/'));
        }

        var directory = pattern[..metaIndex].TrimEnd('/');
        if (directory.Length == 0)
        {
            return "";
        }

        return NormalizePath(directory);
    }

    /// <summary>Go: <c>func watchPatternString(fileSystemWatcher *lsproto.FileSystemWatcher) string</c>。模式的
    /// 可读文本（Pattern 原样，或 <c>base + "/" + pattern</c>），只用于日志。</summary>
    internal static string WatchPatternString(FileSystemWatcher fileSystemWatcher)
    {
        if (fileSystemWatcher.GlobPattern.Pattern is { } pattern)
        {
            return pattern;
        }

        if (fileSystemWatcher.GlobPattern.RelativePattern is { } relativePattern)
        {
            var baseUri = relativePattern.BaseUri.URI is { } uri ? uri.Value : "";
            return baseUri + "/" + relativePattern.Pattern;
        }

        return "";
    }

    /// <summary>
    /// Go: <c>func isRecursiveGlob(fileSystemWatcher *lsproto.FileSystemWatcher) bool</c>。FileSystemWatcher 的模式是否
    /// 要求递归监听（含 <c>**</c> 段）。granular watch 模式发出非递归的 <c>&lt;dir&gt;/*</c>，只监听直接目录。
    /// </summary>
    internal static bool IsRecursiveGlob(FileSystemWatcher fileSystemWatcher) =>
        WatchPatternString(fileSystemWatcher).Contains("**", StringComparison.Ordinal);

    /// <summary>Go: <c>func effectiveKind(fileSystemWatcher *lsproto.FileSystemWatcher) lsproto.WatchKind</c>。</summary>
    internal static WatchKind EffectiveKind(FileSystemWatcher fileSystemWatcher) =>
        fileSystemWatcher.Kind ?? (WatchKind.Create | WatchKind.Change | WatchKind.Delete);

    /// <summary>
    /// Go: <c>func nearestExistingAncestor(fs vfs.FS, dir string) (string, bool)</c>。返回 dir 自身或它的祖先中最深的
    /// 那个已存在目录（一路向上走）。只有整条链（含根）都不存在时 ok 才为 false。
    /// </summary>
    internal static bool TryNearestExistingAncestor(FS fs, string dir, out string ancestor)
    {
        while (true)
        {
            if (fs.DirectoryExists(dir))
            {
                ancestor = dir;
                return true;
            }

            var parent = GetDirectoryPath(dir);
            if (parent == dir)
            {
                ancestor = "";
                return false;
            }

            dir = parent;
        }
    }
}
