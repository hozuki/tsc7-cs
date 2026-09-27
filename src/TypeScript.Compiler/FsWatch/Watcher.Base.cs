// ported from: internal/fswatch/watcher.go:296-1164（watcherImpl / watcherBase / dirWatch / callback 等内部类型）
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `watcherImpl` 是**未导出接口** → C# `internal interface IWatcherImpl`；各后端通过继承 `WatcherBase`
//    拿到 `watchAdd/watchAddMany/watchRemove/handleWatcherError` 的共享实现（Go 是嵌入 watcherBase）。
// 2) Go 的 `b.self.(interface{ subscribeMany(...) error })` 动态判定 → C# 的 `is ISubscribeManyImpl`（接口类型测试，
//    无反射）。
// 3) Go 的 `chan struct{}`（`started`、已关闭即就绪）→ `TaskCompletionSource`（`TrySetResult()` = `close(ch)`）。
//    `startErr` 用 `ExceptionDispatchInfo` 重抛，保住原始栈（Go 是返回值，没有栈的概念）。
// 4) Go 的 `go func(){…}()` → `Task.Run`（`runtime-async=on` 下不产生额外的状态机结构体）。
// 5) Go 的 `map[*dirWatch]struct{}` → `HashSet<DirWatch>`（引用相等语义一致）；`w.state any` → `object?`；
//    `w.sequence func() uint64` → `Func<ulong>?`。
// 6) Go 的 `callback` struct 在 `triggerCallbacks` 里先**值拷贝**进 `cbs`，再回头在原切片里标 `delivered` →
//    C# 用引用类型 `CallbackRegistration`：`cbs` 与原列表指向同一批对象，因此 Go 里"回到原列表按 id 标记"
//    的那段循环在 C# 侧退化为直接 `cb.Delivered = true`（语义等价，且 `mapEventCached` 只读不写）。
// 7) `comparisonPath` 是 mutable struct（见 PathCompare.cs 文件头第 2 条）：`cb.dirComparison` 这类字段按值拷贝，
//    需要写回调用方拷贝的地方用 `ref`。
// 8) Go 的 `dw.addCallback(…) (uint64, bool)` 第二个返回值在 Go 里恒为 true、且所有调用点都丢弃它（`id, _ :=`）
//    → C# 只返回 `ulong`（`entry.Id`）。`dw.watch(…)` 同理。
// 9) Go 的 `sync.Mutex` → `lock (_mu)`；`DirWatch` 的字段是 Go 未导出字段 → C# `internal` + `_camelCase`
//    （后端文件直接读写 `w._dir`/`w._events`/`w._state`）。
namespace TypeScript.FsWatch;

using System.Runtime.ExceptionServices;

/// <summary>Go: <c>type watcherImpl interface</c>。各平台 watcher 实现的内部接口。</summary>
internal interface IWatcherImpl
{
    void Start();

    void Run();

    void Shutdown();

    void WatchAdd(DirWatch w);

    void WatchAddMany(DirWatch[] watches);

    void WatchRemove(DirWatch w);

    void HandleWatcherError(DirWatchError err);

    void Subscribe(DirWatch w);

    void CloseWatch(DirWatch w);
}

/// <summary>Go 里 `self.(interface{ subscribeMany(...) error })` 的可选接口（批量订阅）。</summary>
internal interface ISubscribeManyImpl
{
    void SubscribeMany(DirWatch[] watches);
}

/// <summary>Go: <c>type watcherBase struct</c>。共享的订阅簿记与生命周期逻辑；各后端继承它并覆写
/// <c>Subscribe</c>/<c>CloseWatch</c>/<c>Start</c>。</summary>
internal abstract class WatcherBase : IWatcherImpl
{
    protected readonly object _mu = new();
    private readonly HashSet<DirWatch> _subscriptions = new();
    private readonly TaskCompletionSource _started = new();
    private Exception? _startErr;
    private IWatcherImpl? _self;

    /// <summary>Go: <c>func (b *watcherBase) init(self watcherImpl)</c>。后端构造函数末尾调用 <c>Init(this)</c>。</summary>
    protected void Init(IWatcherImpl self)
    {
        _self = self;
    }

    /// <summary>Go: <c>func (b *watcherBase) notifyStarted()</c>（幂等：已就绪时什么也不做）。</summary>
    protected void NotifyStarted() => _started.TrySetResult();

    /// <summary>Go: <c>func (b *watcherBase) shutdown() {}</c>（默认空实现）。</summary>
    public virtual void Shutdown()
    {
    }

    /// <summary>Go: <c>func (b *watcherBase) run() error</c>。启动后端并等待它就绪；启动失败抛异常。</summary>
    public void Run()
    {
        _ = Task.Run(() =>
        {
            try
            {
                _self!.Start();
            }
            catch (Exception ex)
            {
                HandleStartError(ex);
            }
        });

        _started.Task.Wait();

        Exception? err;
        lock (_mu)
        {
            err = _startErr;
        }

        if (err is not null)
        {
            ExceptionDispatchInfo.Capture(err).Throw();
        }
    }

    private void HandleStartError(Exception err)
    {
        List<DirWatch> subs;
        lock (_mu)
        {
            _startErr = err;
            subs = new List<DirWatch>(_subscriptions);
        }

        foreach (var w in subs)
        {
            w.NotifyError(err);
        }

        NotifyStarted();
    }

    /// <summary>Go: <c>func (b *watcherBase) watchAdd(w *dirWatch) error</c>。</summary>
    public void WatchAdd(DirWatch w) => WatchAddMany([w]);

    /// <summary>Go: <c>func (b *watcherBase) watchAddMany(watches []*dirWatch) error</c>。</summary>
    public void WatchAddMany(DirWatch[] watches)
    {
        lock (_mu)
        {
            var toAdd = new List<DirWatch>(watches.Length);
            foreach (var w in watches)
            {
                if (_subscriptions.Contains(w))
                {
                    continue;
                }

                toAdd.Add(w);
            }

            if (toAdd.Count == 0)
            {
                return;
            }

            if (_self is ISubscribeManyImpl subscriber)
            {
                subscriber.SubscribeMany(toAdd.ToArray());
                foreach (var w in toAdd)
                {
                    _subscriptions.Add(w);
                }

                return;
            }

            var added = new List<DirWatch>(toAdd.Count);
            foreach (var w in toAdd)
            {
                try
                {
                    _self!.Subscribe(w);
                }
                catch
                {
                    foreach (var addedWatch in added)
                    {
                        _subscriptions.Remove(addedWatch);
                        _self!.CloseWatch(addedWatch);
                    }

                    throw;
                }

                _subscriptions.Add(w);
                added.Add(w);
            }
        }
    }

    /// <summary>Go: <c>func (b *watcherBase) watchRemove(w *dirWatch)</c>。</summary>
    public void WatchRemove(DirWatch w)
    {
        lock (_mu)
        {
            if (!_subscriptions.Remove(w))
            {
                return;
            }

            _self!.CloseWatch(w);
        }
    }

    /// <summary>Go: <c>func (b *watcherBase) handleWatcherError(werr *dirWatchError)</c>。</summary>
    public void HandleWatcherError(DirWatchError werr)
    {
        WatchRemove(werr.DirWatch);
        werr.DirWatch.NotifyError(
            new FsWatchException(
                FsWatchErrorKind.WatchTerminated,
                $"fswatch: watch terminated: {werr.Message}",
                werr));
    }

    public abstract void Start();

    public abstract void Subscribe(DirWatch w);

    public abstract void CloseWatch(DirWatch w);
}

/// <summary>Go: <c>type dirWatchError struct</c>。把错误与具体的目录 watch 关联起来。</summary>
internal sealed class DirWatchError(Exception err, DirWatch dirWatch) : Exception(err.Message, err)
{
    internal DirWatch DirWatch => dirWatch;
}

/// <summary>Go: <c>type callback struct</c>。一次逻辑订阅（一个 <see cref="WatchHandle"/>）。</summary>
internal sealed class CallbackRegistration
{
    internal ulong Id;
    internal string Dir = "";
    internal string PhysicalDir = "";
    internal string WatchDir = "";
    internal string WatchPhysicalDir = "";
    internal bool Recursive;
    internal WatchCallback Fn = null!;
    internal Func<string, bool>? Ignore;
    internal ulong SinceSeq;
    internal Exception? Terminal;
    internal bool Delivered;
    internal VolumePathComparer Comparer;
    internal ComparisonPath DirComparison;
    internal ComparisonPath PhysicalComparison;
    internal ComparisonPath FileComparison;

    /// <summary>Go: <c>func (cb callback) mapEvent(e Event) Event</c>。</summary>
    internal Event MapEvent(Event e) => MapEventCached(e, null);

    /// <summary>Go: <c>func (cb callback) mapEventCached(e Event, cache *comparisonCache) Event</c>。</summary>
    internal Event MapEventCached(Event e, ComparisonCache? cache)
    {
        if (PhysicalDir.Length != 0 && (PhysicalDir != Dir || Comparer.IgnoreCase))
        {
            var physicalPath = new ComparisonPath { Path = EventPhysicalPath(e.Path), Cache = cache };
            var root = PhysicalComparison;
            if (root.Path.Length == 0)
            {
                root.Path = PhysicalDir;
            }

            if (Comparer.RebasePrepared(ref physicalPath, root, Dir, out var rebased))
            {
                e = new Event(e.Kind, rebased, e.IncludedWatchRoot);
            }
        }

        return e;
    }

    /// <summary>Go: <c>func (cb callback) eventPhysicalPath(path string) string</c>。</summary>
    internal string EventPhysicalPath(string path)
    {
        if (WatchPhysicalDir.Length != 0 && WatchDir.Length != 0 && WatchPhysicalDir != WatchDir
            && FsWatch.IsInDirectoryOrSelf(WatchDir, path))
        {
            return FsWatch.RebasePath(path, WatchDir, WatchPhysicalDir);
        }

        return path;
    }
}

/// <summary>
/// Go: <c>type dirWatch struct</c>。单个被监听目录的状态：待发事件、已注册回调、共享的 debouncer 引用。
/// </summary>
internal sealed class DirWatch
{
    /// <summary>Go: <c>dirWatch.dir</c>。调用方可见的 watch 根，用于投递路径。</summary>
    internal string _dir;

    /// <summary>Go: <c>dirWatch.physicalDir</c>。传给 OS watcher API 的路径（dir 或其祖先是符号链接时不同）。</summary>
    internal string _physicalDir;

    internal bool _recursive;
    internal EventList _events = new();
    internal VolumePathComparer _comparer;
    internal string _dirFold = "";
    internal string _physicalDirFold = "";

    /// <summary>Go: <c>dirWatch.state any</c>（fsevents/windows 的每目录簿记）。</summary>
    internal object? _state;

    /// <summary>Go: <c>dirWatch.sequence func() uint64</c>（后端事件序号截断点）。</summary>
    internal Func<ulong>? _sequence;

    private readonly object _mu = new();
    private List<CallbackRegistration> _callbacks = [];
    private Debounce? _debounce;
    private ulong _nextCBID;

    /// <summary>
    /// Go: <c>func newDirWatch(dir string, physicalDir string, db *debounce) *dirWatch</c>（构造函数形态：
    /// 注册 debounce 回调 `func() { dw.triggerCallbacks() }`）。
    /// </summary>
    internal DirWatch(string dir, string physicalDir, Debounce db)
    {
        _dir = dir;
        _physicalDir = physicalDir;
        _debounce = db;
        db.Add(this, TriggerCallbacks);
    }

    /// <summary>Go: <c>func (dw *dirWatch) setComparer(comparer pathComparer)</c>。</summary>
    internal void SetComparer(VolumePathComparer comparer)
    {
        _comparer = comparer;
        _dirFold = comparer.Prepare(_dir).Folded;
        _physicalDirFold = _physicalDir == _dir ? _dirFold : comparer.Prepare(_physicalDir).Folded;
    }

    /// <summary>Go: <c>func (dw *dirWatch) displayPath(watchPath string) string</c>。</summary>
    internal string DisplayPath(string watchPath) => FsWatch.RebasePath(watchPath, _physicalDir, _dir);

    /// <summary>Go: <c>func (dw *dirWatch) physicalPath(displayPath string) string</c>。</summary>
    internal string PhysicalPath(string displayPath) => FsWatch.RebasePath(displayPath, _dir, _physicalDir);

    /// <summary>Go: <c>func (dw *dirWatch) destroyDebounce()</c>。</summary>
    internal void DestroyDebounce()
    {
        Debounce? db;
        lock (_mu)
        {
            db = _debounce;
            _debounce = null;
        }

        db?.Remove(this);
    }

    /// <summary>Go: <c>func (dw *dirWatch) notify()</c>。</summary>
    internal void Notify()
    {
        bool hasPendingCBs;
        bool hasTerminal;
        bool hasEvents;
        bool hasError;
        Debounce? db;
        lock (_mu)
        {
            hasPendingCBs = false;
            hasTerminal = false;
            foreach (var cb in _callbacks)
            {
                if (cb.Delivered)
                {
                    continue;
                }

                hasPendingCBs = true;
                if (cb.Terminal is not null)
                {
                    hasTerminal = true;
                }
            }

            hasEvents = _events.Size() > 0;
            hasError = _events.HasError();
            db = _debounce;
        }

        if (hasPendingCBs && (hasEvents || hasError || hasTerminal) && db is not null)
        {
            db.Trigger();
        }
    }

    /// <summary>Go: <c>func (dw *dirWatch) notifyError(err error)</c>。</summary>
    internal void NotifyError(Exception err)
    {
        List<CallbackRegistration> cbs;
        lock (_mu)
        {
            cbs = _callbacks;
            _callbacks = [];
        }

        foreach (var cb in cbs)
        {
            cb.Fn(null, err);
        }
    }

    /// <summary>Go: <c>func (dw *dirWatch) triggerCallbacks()</c>。</summary>
    internal void TriggerCallbacks()
    {
        List<CallbackRegistration> cbs;
        Event[][] eventsByCallback;
        Exception? err;
        lock (_mu)
        {
            var hasError = _events.HasError();
            var hasEvents = _events.Size() > 0;
            cbs = [];
            var hasTerminal = false;
            foreach (var cb in _callbacks)
            {
                if (cb.Delivered)
                {
                    continue;
                }

                if (cb.Terminal is not null)
                {
                    hasTerminal = true;
                }

                cbs.Add(cb);
            }

            if (cbs.Count == 0)
            {
                if (hasEvents || hasError)
                {
                    _ = _events.Drain();
                }

                return;
            }

            if (!hasEvents && !hasError && !hasTerminal)
            {
                return;
            }

            var startSeqs = new ulong[cbs.Count];
            for (var i = 0; i < cbs.Count; i++)
            {
                startSeqs[i] = cbs[i].SinceSeq;
            }

            (eventsByCallback, err) = _events.DrainForSequences(startSeqs);
            for (var i = 0; i < cbs.Count; i++)
            {
                if (cbs[i].Terminal is not null)
                {
                    cbs[i].Delivered = true;
                }
            }
        }

        // Go 在这里已 unlock，回调不得在锁内执行。
        TriggerCallbacksDeliver(cbs, eventsByCallback, err);
    }

    private void TriggerCallbacksDeliver(List<CallbackRegistration> cbs, Event[][] eventsByCallback, Exception? err)
    {
        var comparisons = new ComparisonCache();
        for (var i = 0; i < cbs.Count; i++)
        {
            var cb = cbs[i];
            var cbEvents = eventsByCallback[i];
            if (cb.Ignore is not null || !cb.Recursive || cb.Dir != _dir || cb.FileComparison.Path.Length != 0)
            {
                var filtered = new List<Event>(cbEvents.Length);
                foreach (var rawEvent in cbEvents)
                {
                    var e = cb.MapEventCached(rawEvent, comparisons);
                    if (cb.FileComparison.Path.Length != 0)
                    {
                        var path = new ComparisonPath { Path = e.Path, Cache = comparisons };
                        var fileComparison = cb.FileComparison;
                        if (!cb.Comparer.SuffixPrepared(fileComparison, ref path, out var suffix) || suffix.Length != 0)
                        {
                            continue;
                        }

                        e = new Event(e.Kind, cb.FileComparison.Path, e.IncludedWatchRoot);
                    }

                    if (cb.Ignore is not null && cb.Ignore(e.Path))
                    {
                        continue;
                    }

                    if (cb.Dir != _dir && !e.IncludedWatchRoot && e.Path == cb.Dir && e.Kind == EventKind.Update)
                    {
                        continue;
                    }

                    if (cb.Recursive)
                    {
                        if (cb.Dir != _dir && !FsWatch.IsInDirectoryOrSelf(cb.Dir, e.Path))
                        {
                            continue;
                        }
                    }
                    else if (!FsWatch.IsDirectChild(cb.Dir, e.Path) && !(cb.Dir != _dir && e.Path == cb.Dir))
                    {
                        continue;
                    }

                    filtered.Add(e);
                }

                cbEvents = filtered.ToArray();
            }

            var cbErr = err;
            if (cb.Terminal is not null)
            {
                cbErr = cb.Terminal;
            }

            if (cbEvents.Length > 0 || cbErr is not null)
            {
                cb.Fn(cbEvents, cbErr);
            }
        }
    }

    /// <summary>Go: <c>func (dw *dirWatch) terminateCallbacksForDeletedRoot(path string, seq uint64, err error) bool</c>。</summary>
    internal bool TerminateCallbacksForDeletedRoot(string path, ulong seq, Exception err)
    {
        lock (_mu)
        {
            var changed = false;
            var comparisons = new ComparisonCache();
            var deleted = new ComparisonPath { Path = path, Cache = comparisons };
            foreach (var cb in _callbacks)
            {
                if (cb.Delivered || cb.Terminal is not null || cb.SinceSeq >= seq)
                {
                    continue;
                }

                var physicalPath = new ComparisonPath { Path = cb.EventPhysicalPath(path), Cache = comparisons };
                var dir = cb.DirComparison;
                var physical = cb.PhysicalComparison;
                var logicalMatch = cb.Comparer.SuffixPrepared(deleted, ref dir, out _);
                var physicalMatch = cb.Comparer.SuffixPrepared(physicalPath, ref physical, out _);
                if (logicalMatch || physicalMatch)
                {
                    cb.Terminal = err;
                    changed = true;
                }
            }

            return changed;
        }
    }

    /// <summary>Go: <c>func (dw *dirWatch) watch(dir, physicalDir string, recursive bool, fn WatchCallback,
    /// ignore func(path string) bool) (uint64, bool)</c>。Go 的第二个返回值恒为 true，C# 只返回 id（见文件头第 8 条）。</summary>
    internal ulong Watch(string dir, string physicalDir, bool recursive, WatchCallback fn, Func<string, bool>? ignore) =>
        AddCallback(dir, physicalDir, recursive, fn, ignore, "");

    /// <summary>Go: <c>func (dw *dirWatch) addCallback(…) (uint64, bool)</c>。</summary>
    internal ulong AddCallback(
        string dir,
        string physicalDir,
        bool recursive,
        WatchCallback fn,
        Func<string, bool>? ignore,
        string file)
    {
        lock (_mu)
        {
            _nextCBID++;
            var id = _nextCBID;
            var sinceSeq = _events.Sequence();
            if (_sequence is not null)
            {
                sinceSeq = _sequence();
            }

            _callbacks.Add(new CallbackRegistration
            {
                Id = id,
                Dir = dir,
                PhysicalDir = physicalDir,
                WatchDir = _dir,
                WatchPhysicalDir = _physicalDir,
                Recursive = recursive,
                Fn = fn,
                Ignore = ignore,
                SinceSeq = sinceSeq,
                Comparer = _comparer,
                DirComparison = _comparer.Prepare(dir),
                PhysicalComparison = _comparer.Prepare(physicalDir),
                FileComparison = _comparer.Prepare(file),
            });

            return id;
        }
    }

    /// <summary>Go: <c>func (dw *dirWatch) unwatch(id uint64) bool</c>（返回是否已无回调）。</summary>
    internal bool Unwatch(ulong id)
    {
        lock (_mu)
        {
            for (var i = 0; i < _callbacks.Count; i++)
            {
                if (_callbacks[i].Id == id)
                {
                    _callbacks.RemoveAt(i);
                    return _callbacks.Count == 0;
                }
            }

            return false;
        }
    }

    /// <summary>Go: <c>func (dw *dirWatch) unref(w *watcher)</c>。</summary>
    internal void Unref(FsWatcher w)
    {
        bool empty;
        lock (_mu)
        {
            empty = _callbacks.Count == 0;
        }

        if (empty)
        {
            w.RemoveDirWatch(this);
        }
    }
}
