// ported from: internal/execute/watchmanager/watchmanager.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的包级函数 `NewWatchManager`/`NewDirWatchSet` 与类型 `WatchManager` 同包（Go 里包名与类型名不冲突），C# 不允许
//    与类型同名的静态类 → 按仓库/调用方约定改成 `WatchManager` **类型**上的 public static 成员（watchbackend.go 的三个
//    包级函数同理，见 WatchBackend.cs 偏离 10），因此 `WatchManager` 声明为 `partial`（另一半在同目录 WatchBackend.cs）。
//    调用点写 `using static TypeScript.Execute.WatchManager.WatchManager;` 后与 Go 的包级调用同形。
//    **消费方注意**：命名空间名与类型名都是 `WatchManager`，在别的命名空间里简单名 `WatchManager` 会先命中外层命名空间
//    `TypeScript.Execute.WatchManager` 而报 CS0234 → 必须把 `using TypeScript.Execute.WatchManager;` 写在 file-scoped
//    namespace **之内**（同 Checker.C6.ApparentTypes.cs 偏离 17/18）。
// 2) Go 的 `chan struct{}`（容量 1：非阻塞发送 + 合并 + 阻塞接收）→ `SemaphoreSlim(0, 1)`：
//    - `select { case wm.doCycleCh <- struct{}{}: … ; default: }`（发送成功即返回、已满即合并）→
//      `try { _doCycleCh.Release(); } catch (SemaphoreFullException) { }`；
//    - `select { case <-ctx.Done(): …; case <-wm.doCycleCh: … }`（阻塞接收）→ `RunLoop` 里的
//      `WaitHandle.WaitAny([_doCycleCh.AvailableWaitHandle, ctx.CancellationToken.WaitHandle])`：下标 1（ctx 取消）→
//      `CloseAllWatches(); return;`；下标 0 → `_doCycleCh.Wait()` 消耗掉信号，再 `doCycle()`。循环里的 `case` 语义与
//      Go 一致（每次只处理一个就绪分支）。
//      **语义差异**：Go 的 select 在两个 case 同时就绪时**随机**选一个；`WaitAny` 固定返回最小下标 → "ctx 取消的同时
//      还有待处理信号"时 C# 总会先跑一次 `doCycle()` 再退出（Go 也可能直接退出）。只有单个信号/单个取消的正常路径一致。
// 3) `context.Context` → `TypeScript.Core.Context`（`ctx.Done()` → `ctx.CancellationToken`；`Context.Background` 对应
//    CancellationToken.None，即 Go 里永不触发的 Done channel）。两个 WaitHandle 在循环外取一次——Go 每轮 select 读的是
//    同一个 channel，值不变。
// 4) `sync.Mutex` → `lock (_mu)`（本仓库写法）；但 `Lock`/`Unlock` 是公开 API（Go 用 `wm.mu.Lock()/Unlock()` 跨方法持锁，
//    见 Locking contract）→ 实现为 `Monitor.Enter(_mu)`/`Monitor.Exit(_mu)`。**差异**：`Monitor` 可重入，Go 的 `sync.Mutex`
//    不可重入；TSGO 的调用点都成对使用、不重入（DoCycle 体内也不会再调 Lock），所以行为一致。
// 5) `io.Writer` → `System.IO.TextWriter`；`io.Closer` → `TypeScript.FsWatch.Watch`；`fswatch.EventKind` → `EventKind`
//    （Go 的 `%s` 走 `EventKind.String()` → `Fsw.String(kind)`）。Go 的 `%v` 打 bool 得到 "true"/"false"，C# 的插值是
//    "True"/"False" → 日志里显式写 `x ? "true" : "false"`（同 Debug/Debug.cs 的 Format）。
// 6) `fmt.Fprintf(w, "…%v…\n", err)` → `w.Write($"…{err.Message}\n")`：Go 的 `%v` 打 error 得到的是 Error() 文本，
//    C# 里最接近的是 `Exception.Message`（`ToString()` 会带上类型名与栈）。换行坚持显式 "\n"（同 Execute/Tsc/Statistics.cs
//    的 `w.Write($"…\n")`）；`fmt.Fprintln(wm.DebugLog)` 对应 `DebugLog.Write("\n")`。
// 7) Go 未导出方法 `signalDoCycle`/`onWatchEvents`/`handleWatchTerminated`/`createDirWatchRequest`/`createDirWatches`
//    → private PascalCase；未导出类型 `watchedDir`/`dirWatchUpdate` → `WatchedDir`/`DirWatchUpdate`；未导出字段
//    `closer`/`recursive`/`dir` → `_closer`/`_recursive`/`_dir`（architecture.md §2）；`canonical` → private `Canonical`。
// 8) `ReconcileWatches`/`createDirWatches` 的 error → 异常（§3.2）：Go 的调用点是"打印错误 + ForceOverflow()"，C# 调用点
//    改成 try/catch；`createDirWatches` 失败时仍按 Go 逐目录写 DebugLog，之后 `throw;` 重新抛出。
// 9) `core.DiffMapsFunc` → `Core.DiffMapsFunc`（别名 `Core` = `TypeScript.Core.Core`；`Core` 同时是命名空间名，别名必须写在
//    file-scoped namespace **之内**，理由同偏离 1 的说明）。**关键差异**：Go 的 `onRemoved`/`onChanged` 回调里直接
//    `delete(wm.watchedDirs, dir)`（Go 允许在 range 中删除），而 C# 的 `Dictionary` 在 foreach 中被修改会抛
//    InvalidOperationException → 回调里只把 `(dir, wd)` 记进 `removals`，等 `DiffMapsFunc` 返回后再统一 Close + Remove。
//    日志仍在回调内按原顺序输出；DiffMapsFunc 先遍历 m2 再遍历 m1、每个键只回调一次，且 `_watchedDirs` 只在同一把锁下
//    变动，所以延后执行的可观察行为与 Go 一致（闭合顺序与"createDirWatches 之前 watch 集合已清干净"也一致）。
// 10) Go 的 `(map[string]fswatch.EventKind, bool)` 多返回值 → `void DrainEvents(out Dictionary<string, EventKind>? changed,
//     out bool overflow)`：bool 是"事件溢出"标记、不是 ok 语义，故不套 `TryX` 形式；调用点
//     `DrainEvents(out var changedPaths, out var overflow)` 与 Go 的 `changedPaths, overflow := wm.DrainEvents()` 同形。
//     Go 的 nil map 保持 `?`（消费点只做 len/range）。
// 11) `CloseAllWatches`/`ReconcileWatches` 里 Go 一边 range 一边 `delete` 同一个 map → C# 先收集再删（Dictionary 不允许
//     遍历中修改）；Go 的循环结束时 map 同样是空的。
// 12) `createDirWatches(updates []dirWatchUpdate)`：Go 用 append 构造的切片 → C# `List<DirWatchUpdate>`（§3.2 的
//     "append 构造的切片用 List<T>"），`appends = append(additions, changes...)` → `List.AddRange`。
// 13) `ComparePathsOptions`/`GetCanonicalFileName`/`GetRootLength`/`GetDirectoryPath`/`ContainsPath` 来自静态类
//     `TypeScript.TSPath.TSPath`（`using static` 后裸名调用，同 Execute/Tsc/Emit.cs）。注意 `Path` 在 TSPath 里是 Go 的
//     `type Path string`（record struct），不是工具类；`System.IO.Path` 由 ImplicitUsings 提供，本文件不用它。
// 14) Go 的 `DebugLog io.Writer` 是导出字段 → `public TextWriter? DebugLog`，nil 时逐处显式判空（与 Go 一致）。
// 15) `DoCycleCh() <-chan struct{}` 的返回类型 → `SemaphoreSlim`（Go 里本包之外没有调用点；返回的就是偏离 2 的底层信号量）。
namespace TypeScript.Execute.WatchManager;

using TypeScript.Core;
using TypeScript.FsWatch;
using TypeScript.TSPath;
using Core = TypeScript.Core.Core;
using Fsw = TypeScript.FsWatch.FsWatch;
using static TypeScript.TSPath.TSPath;

/// <summary>Go: <c>type watchedDir struct</c>（watchmanager.go:15-18）。</summary>
internal sealed class WatchedDir
{
    /// <summary>Go: <c>closer io.Closer</c>（createDirWatches 成功时才赋值）。</summary>
    internal Watch? _closer;

    /// <summary>Go: <c>recursive bool</c>。</summary>
    internal bool _recursive;
}

/// <summary>Go: <c>type dirWatchUpdate struct</c>（watchmanager.go:20-23）。</summary>
internal sealed class DirWatchUpdate
{
    /// <summary>Go: <c>dir string</c>。</summary>
    internal string _dir = "";

    /// <summary>Go: <c>recursive bool</c>。</summary>
    internal bool _recursive;
}

/// <summary>
/// Go: <c>type WatchManager struct</c>（watchmanager.go:25-48）。管理 fswatch 目录监听、事件累积与 DoCycle 信号；
/// CLI watcher 与 build mode orchestrator 共用。
///
/// Locking contract:
///   - Call Lock/Unlock around the entire DoCycle body.
///   - ReconcileWatches must be called under Lock.
///   - CloseAllWatches and handleWatchTerminated manage their own locking.
/// </summary>
public sealed partial class WatchManager
{
    private readonly object _mu = new();
    private WatchBackend? _backend;
    private readonly Dictionary<string, WatchedDir> _watchedDirs = new();
    private readonly SemaphoreSlim _doCycleCh = new(0, 1);

    /// <summary>Go: <c>DebugLog io.Writer</c>（导出字段）：非 nil 时接收详细的 watch 诊断信息。</summary>
    public TextWriter? DebugLog;

    private readonly TextWriter _warnWriter;
    private readonly Func<string, bool> _dirExists;

    private readonly object _changedMu = new();
    private Dictionary<string, EventKind>? _changedPaths;
    private bool _changedOverflow;

    internal WatchManager(TextWriter warnWriter, Func<string, bool> dirExists)
    {
        _warnWriter = warnWriter;
        _dirExists = dirExists;
    }

    /// <summary>Go: <c>func NewWatchManager(warnWriter io.Writer, dirExists func(string) bool) *WatchManager</c>（watchmanager.go:50-58）。</summary>
    public static WatchManager NewWatchManager(TextWriter warnWriter, Func<string, bool> dirExists) =>
        new(warnWriter, dirExists);

    /// <summary>Go: <c>func (wm *WatchManager) SetBackend(b WatchBackend)</c>（watchmanager.go:59）。</summary>
    public void SetBackend(WatchBackend b) => _backend = b;

    /// <summary>Go: <c>func (wm *WatchManager) Backend() WatchBackend</c>（watchmanager.go:61）。Go 里未设置时是 nil。</summary>
    public WatchBackend? Backend() => _backend;

    /// <summary>Go: <c>func (wm *WatchManager) EnsureDefaultBackend()</c>（watchmanager.go:63-71）。</summary>
    public void EnsureDefaultBackend()
    {
        if (_backend == null)
        {
            var fsw = Fsw.Default();
            _backend = new FSWatchBackend { Inner = fsw };
            if (DebugLog != null)
            {
                DebugLog.Write($"[watch] using {fsw.Name()} backend\n");
            }
        }
    }

    /// <summary>Go: <c>func (wm *WatchManager) Lock()</c>（watchmanager.go:73）。</summary>
    public void Lock() => Monitor.Enter(_mu);

    /// <summary>Go: <c>func (wm *WatchManager) Unlock()</c>（watchmanager.go:75）。</summary>
    public void Unlock() => Monitor.Exit(_mu);

    /// <summary>Go: <c>func (wm *WatchManager) DoCycleCh() &lt;-chan struct{}</c>（watchmanager.go:77）。
    /// Go 的 channel → <see cref="SemaphoreSlim"/>（见文件头偏离 2/15）。</summary>
    public SemaphoreSlim DoCycleCh() => _doCycleCh;

    /// <summary>Go: <c>func (wm *WatchManager) DrainEvents() (changed map[string]fswatch.EventKind, overflow bool)</c>（watchmanager.go:79-87）。</summary>
    public void DrainEvents(out Dictionary<string, EventKind>? changed, out bool overflow)
    {
        lock (_changedMu)
        {
            changed = _changedPaths;
            overflow = _changedOverflow;
            _changedPaths = null;
            _changedOverflow = false;
        }
    }

    /// <summary>Go: <c>func (wm *WatchManager) ForceOverflow()</c>（watchmanager.go:89-93）。</summary>
    public void ForceOverflow()
    {
        lock (_changedMu)
        {
            _changedOverflow = true;
        }
    }

    /// <summary>Go: <c>func (wm *WatchManager) signalDoCycle()</c>（watchmanager.go:95-102）：容量 1 的非阻塞发送，
    /// 已满（已有待处理信号）就合并掉。</summary>
    private void SignalDoCycle()
    {
        try
        {
            // Signal sent; the DoCycle loop will pick it up.
            _doCycleCh.Release();
        }
        catch (SemaphoreFullException)
        {
            // A signal is already pending; coalesced.
        }
    }

    /// <summary>Go: <c>func (wm *WatchManager) onWatchEvents(events []fswatch.Event, err error)</c>（watchmanager.go:104-145）。</summary>
    private void OnWatchEvents(Event[]? events, Exception? err)
    {
        if (err != null)
        {
            if (FsWatchException.Is(err, FsWatchErrorKind.Overflow))
            {
                if (DebugLog != null)
                {
                    DebugLog.Write("[watch] event overflow, triggering rebuild\n");
                }
                lock (_changedMu)
                {
                    _changedOverflow = true;
                }
                SignalDoCycle();
                return;
            }
            _warnWriter.Write($"Warning: File watch error: {err.Message}\n");
            return;
        }

        if (events is { Length: > 0 })
        {
            if (DebugLog != null)
            {
                DebugLog.Write($"[watch] {events.Length} event(s): ");
                for (var i = 0; i < events.Length; i++)
                {
                    if (i > 0)
                    {
                        DebugLog.Write(", ");
                    }
                    if (i >= 5)
                    {
                        DebugLog.Write($"... and {events.Length - i} more");
                        break;
                    }
                    DebugLog.Write($"{Fsw.String(events[i].Kind)} {events[i].Path}");
                }
                DebugLog.Write("\n");
            }
            lock (_changedMu)
            {
                if (_changedPaths == null)
                {
                    _changedPaths = new Dictionary<string, EventKind>(events.Length);
                }
                foreach (var e in events)
                {
                    _changedPaths[e.Path] = e.Kind;
                }
            }
            SignalDoCycle();
        }
    }

    /// <summary>Go: <c>func (wm *WatchManager) handleWatchTerminated(dir string, identity *watchedDir)</c>（watchmanager.go:147-165）。
    /// 自管锁（见 Locking contract）。</summary>
    private void HandleWatchTerminated(string dir, WatchedDir identity)
    {
        if (DebugLog != null)
        {
            DebugLog.Write($"[watch] watch terminated: {dir}\n");
        }
        Watch? staleCloser = null;
        lock (_mu)
        {
            if (_watchedDirs.TryGetValue(dir, out var wd) && wd == identity)
            {
                staleCloser = wd._closer;
                _watchedDirs.Remove(dir);
            }
        }
        staleCloser?.Close();
        lock (_changedMu)
        {
            _changedOverflow = true;
        }
        SignalDoCycle();
    }

    /// <summary>Go: <c>func (wm *WatchManager) CloseAllWatches()</c>（watchmanager.go:167-178）。自管锁（见 Locking contract）。</summary>
    public void CloseAllWatches()
    {
        var closers = new List<Watch>(_watchedDirs.Count);
        lock (_mu)
        {
            foreach (var wd in _watchedDirs.Values)
            {
                closers.Add(wd._closer!);
            }
            _watchedDirs.Clear();
        }
        foreach (var c in closers)
        {
            c.Close();
        }
    }

    /// <summary>Go: <c>func (wm *WatchManager) createDirWatchRequest(dir string, entry *watchedDir) WatchDirectoryRequest</c>（watchmanager.go:180-193）。</summary>
    private WatchDirectoryRequest CreateDirWatchRequest(string dir, WatchedDir entry) => new()
    {
        Dir = dir,
        Recursive = entry._recursive,
        Ignore = ShouldIgnoreWatchPath,
        Callback = (events, err) =>
        {
            if (err != null && FsWatchException.Is(err, FsWatchErrorKind.WatchTerminated))
            {
                HandleWatchTerminated(dir, entry);
                return;
            }
            OnWatchEvents(events, err);
        },
    };

    /// <summary>Go: <c>func (wm *WatchManager) ResolveDesiredDirs(desiredDirs map[string]bool) map[string]bool</c>（watchmanager.go:195-224）。</summary>
    public Dictionary<string, bool> ResolveDesiredDirs(Dictionary<string, bool> desiredDirs)
    {
        var resolved = new Dictionary<string, bool>(desiredDirs.Count);
        foreach (var (dir, recursive) in desiredDirs)
        {
            var watchDir = dir;
            var watchRecursive = recursive;
            while (!_dirExists(watchDir))
            {
                var parent = GetDirectoryPath(watchDir);
                if (parent == watchDir)
                {
                    break;
                }
                watchDir = parent;
                watchRecursive = false; // ancestor fallbacks are always non-recursive
            }
            if (!_dirExists(watchDir) || !CanWatchDirectory(watchDir))
            {
                if (DebugLog != null)
                {
                    DebugLog.Write($"[watch] no watchable ancestor for {dir}\n");
                }
                continue;
            }
            if (watchDir != dir && DebugLog != null)
            {
                DebugLog.Write($"[watch] resolved {dir} to ancestor {watchDir}\n");
            }
            resolved[watchDir] = resolved.TryGetValue(watchDir, out var existing) && existing || watchRecursive;
        }
        return resolved;
    }

    /// <summary>Go: <c>func (wm *WatchManager) ReconcileWatches(desiredDirs map[string]bool) error</c>（watchmanager.go:226-262）。
    /// 必须在持有 <see cref="Lock"/> 时调用（见 Locking contract）；失败抛异常（Go 的 error，见文件头偏离 8）。</summary>
    public void ReconcileWatches(Dictionary<string, bool> desiredDirs)
    {
        if (_backend == null)
        {
            return;
        }

        var additions = new List<DirWatchUpdate>();
        var changes = new List<DirWatchUpdate>();

        // Go 在下面两个回调里就地 delete(wm.watchedDirs, dir)；C# 的 Dictionary 不允许遍历中修改，
        // 于是先记下要关掉的 (dir, wd)，DiffMapsFunc 返回后再统一 Close + Remove（见文件头偏离 9/11）。
        var removals = new List<(string Dir, WatchedDir Wd)>();

        Core.DiffMapsFunc(
            _watchedDirs,
            desiredDirs,
            (wd, recursive) => wd._recursive == recursive,
            onAdded: (dir, recursive) =>
            {
                if (DebugLog != null)
                {
                    DebugLog.Write($"[watch] watching directory {dir} (recursive={(recursive ? "true" : "false")})\n");
                }
                additions.Add(new DirWatchUpdate { _dir = dir, _recursive = recursive });
            },
            onRemoved: (dir, wd) =>
            {
                if (DebugLog != null)
                {
                    DebugLog.Write($"[watch] closing stale dir watch: {dir}\n");
                }
                removals.Add((dir, wd));
            },
            onChanged: (dir, wd, recursive) =>
            {
                if (DebugLog != null)
                {
                    DebugLog.Write(
                        $"[watch] recreating dir watch {dir} (recursive {(wd._recursive ? "true" : "false")}→{(recursive ? "true" : "false")})\n");
                }
                removals.Add((dir, wd));
                changes.Add(new DirWatchUpdate { _dir = dir, _recursive = recursive });
            });

        foreach (var (dir, wd) in removals)
        {
            wd._closer!.Close();
            _watchedDirs.Remove(dir);
        }
        additions.AddRange(changes);
        CreateDirWatches(additions);
    }

    /// <summary>Go: <c>func (wm *WatchManager) createDirWatches(updates []dirWatchUpdate) error</c>（watchmanager.go:264-289）。
    /// 失败抛异常（Go 的 error，见文件头偏离 8）。</summary>
    private void CreateDirWatches(List<DirWatchUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return;
        }
        var requests = new WatchDirectoryRequest[updates.Count];
        var entries = new WatchedDir[updates.Count];
        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];
            var entry = new WatchedDir { _recursive = update._recursive };
            entries[i] = entry;
            requests[i] = CreateDirWatchRequest(update._dir, entry);
        }
        Watch[] closers;
        try
        {
            closers = _backend!.WatchDirectories(requests);
        }
        catch (Exception err)
        {
            if (DebugLog != null)
            {
                foreach (var update in updates)
                {
                    DebugLog.Write($"[watch] failed to watch directory {update._dir}: {err.Message}\n");
                }
            }
            throw;
        }
        for (var i = 0; i < updates.Count; i++)
        {
            entries[i]._closer = closers[i];
            _watchedDirs[updates[i]._dir] = entries[i];
        }
    }

    /// <summary>Go: <c>func NewDirWatchSet(opts tspath.ComparePathsOptions) *DirWatchSet</c>（watchmanager.go:301-307）。</summary>
    public static DirWatchSet NewDirWatchSet(ComparePathsOptions opts) => new(opts);

    /// <summary>Go: <c>func (wm *WatchManager) IsPathUnderWatch(path string, opts tspath.ComparePathsOptions) bool</c>（watchmanager.go:345-352）。</summary>
    public bool IsPathUnderWatch(string path, ComparePathsOptions opts)
    {
        foreach (var dir in _watchedDirs.Keys)
        {
            if (ContainsPath(dir, path, opts))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (wm *WatchManager) RunLoop(ctx context.Context, doCycle func())</c>（watchmanager.go:354-364）。
    /// Go 的 `select { case &lt;-ctx.Done(): …; case &lt;-wm.doCycleCh: … }` → <see cref="WaitHandle.WaitAny"/>（见文件头偏离 2）。</summary>
    public void RunLoop(Context ctx, Action doCycle)
    {
        var doCycleHandle = _doCycleCh.AvailableWaitHandle;
        var cancelledHandle = ctx.CancellationToken.WaitHandle;
        while (true)
        {
            var index = WaitHandle.WaitAny([doCycleHandle, cancelledHandle]);
            if (index == 1)
            {
                CloseAllWatches();
                return;
            }
            _doCycleCh.Wait();
            doCycle();
        }
    }
}

/// <summary>
/// Go: <c>type DirWatchSet struct</c>（watchmanager.go:291-299）。累积"应该被监听"的目录集合，同时高效回答覆盖查询：
/// 一个目录"被覆盖"指它已在集合中，或它位于集合中某个**递归**监听目录之下。
/// </summary>
public sealed class DirWatchSet
{
    private readonly ComparePathsOptions _opts;
    private readonly Dictionary<string, bool> _dirs = new();

    /// <summary>Go: <c>names map[string]string</c>：canonical 名 → 首次写入时的原始名。</summary>
    private readonly Dictionary<string, string> _names = new();

    internal DirWatchSet(ComparePathsOptions opts)
    {
        _opts = opts;
    }

    /// <summary>Go: <c>func (s *DirWatchSet) canonical(dir string) string</c>（watchmanager.go:309-311，未导出 → private）。</summary>
    private string Canonical(string dir) => GetCanonicalFileName(dir, _opts.UseCaseSensitiveFileNames);

    /// <summary>Go: <c>func (s *DirWatchSet) Set(dir string, recursive bool)</c>（watchmanager.go:313-320）。</summary>
    public void Set(string dir, bool recursive)
    {
        var original = dir;
        dir = Canonical(dir);
        if (!_names.ContainsKey(dir))
        {
            _names[dir] = original;
        }
        // Go: s.dirs[dir] = s.dirs[dir] || recursive（缺失键的零值是 false，且递归标记只升不降）。
        _dirs[dir] = _dirs.TryGetValue(dir, out var existing) && existing || recursive;
    }

    /// <summary>Go: <c>func (s *DirWatchSet) Covered(dir string) bool</c>（watchmanager.go:322-335）。</summary>
    public bool Covered(string dir)
    {
        dir = Canonical(dir);
        if (_dirs.ContainsKey(dir))
        {
            return true;
        }
        var rootLength = GetRootLength(dir);
        while (dir.Length > rootLength)
        {
            dir = GetDirectoryPath(dir);
            if (_dirs.TryGetValue(dir, out var recursive) && recursive)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (s *DirWatchSet) Dirs() map[string]bool</c>（watchmanager.go:337-343）：canonical 名换回原始名。</summary>
    public Dictionary<string, bool> Dirs()
    {
        var dirs = new Dictionary<string, bool>(_dirs.Count);
        foreach (var (key, recursive) in _dirs)
        {
            dirs[_names[key]] = recursive;
        }
        return dirs;
    }
}
