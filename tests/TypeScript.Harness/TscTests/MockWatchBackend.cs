// ported from: internal/execute/tsctests/mock_watch_backend.go
//
// 与 Go 的偏离（逐条）：
// 1) `io.Closer` → 本文件自己的 <see cref="MockWatch"/> 实现 <see cref="Watch"/>（`Close()` 在 C# 的
//    fswatch 移植里返回 void，见 Execute/WatchManager/WatchBackend.cs 文件头第 1 条）。
// 2) `(io.Closer, error)` → 只返回值、失败抛异常（§3.2）。Go 的 "directory does not exist" 错误在这里是
//    `FsWatchException`（C# 的 WatchManager 对 `WatchDirectories` 的异常是"记录 + 继续上抛"，与 Go 的
//    `return err` 逐条对应）。
// 3) Go 的 <c>path.Dir</c>/<c>path</c>（POSIX）→ `TSPath.GetDirectoryPath`（同一语义，见文件头的路径纪律）。
// 4) `strings.HasPrefix`/`Contains`/`sort.Strings` → 各自的序数版本（Go 的字符串比较是字节序）。
// 5) `sync.Mutex` → `lock (_mu)`；Go 里"先加锁取快照、再在锁外回调"的写法逐字保留（避免回调重入死锁）。
namespace TypeScript.Harness.TscTests;

using global::System;
using global::System.Collections.Generic;
using global::System.Text;
using TypeScript.FsWatch;
using TypeScript.TestUtil.FsBaselineUtil;
using TypeScript.TSPath;
using TypeScript.Execute.WatchManager;

using static TypeScript.TSPath.TSPath;
using FileChange = TypeScript.TestUtil.FsBaselineUtil.FSDiffer.FileChange;
using WmWatchBackend = TypeScript.Execute.WatchManager.WatchBackend;
using WmWatchDirectoryRequest = TypeScript.Execute.WatchManager.WatchDirectoryRequest;

/// <summary>
/// Go: <c>type MockWatchBackend struct</c>。记录所有 <c>WatchDirectory</c> 调用，测试可据此校验注册的 watch
/// 是否正确；<c>SendEvents</c> 只把事件投递给路径匹配的 watch（匹配不到就静默丢弃，故意如此）。
/// </summary>
public sealed class MockWatchBackend : WmWatchBackend
{
    private readonly object _mu = new();

    /// <summary>Go: <c>Dirs map[string]*MockWatch</c>。</summary>
    public readonly Dictionary<string, MockWatch> Dirs = new();

    /// <summary>Go: <c>DirectoryExists func(string) bool</c>；非 nil 时 watch 一个不存在的目录会失败。</summary>
    public Func<string, bool>? DirectoryExists;

    /// <summary>Go: <c>UseCaseSensitiveFileNames bool</c>。</summary>
    public bool UseCaseSensitiveFileNames;

    /// <summary>Go: <c>func (m *MockWatchBackend) HasWatches() bool</c>。</summary>
    public bool HasWatches()
    {
        lock (_mu)
        {
            return Dirs.Count > 0;
        }
    }

    /// <summary>Go: <c>func (m *MockWatchBackend) WatchDirectory(dir string, fn fswatch.WatchCallback, recursive bool, ignore func(string) bool) (io.Closer, error)</c>。</summary>
    public Watch WatchDirectory(string dir, WatchCallback fn, bool recursive, Func<string, bool>? ignore)
    {
        var closers = WatchDirectories([new WmWatchDirectoryRequest
        {
            Dir = dir,
            Callback = fn,
            Recursive = recursive,
            Ignore = ignore,
        }]);
        return closers[0];
    }

    /// <summary>Go: <c>func (m *MockWatchBackend) WatchDirectories(requests []watchmanager.WatchDirectoryRequest) ([]io.Closer, error)</c>。</summary>
    public Watch[] WatchDirectories(WmWatchDirectoryRequest[] requests)
    {
        lock (_mu)
        {
            foreach (var request in requests)
            {
                if (DirectoryExists is not null && !DirectoryExists(request.Dir))
                {
                    throw new FsWatchException(FsWatchErrorKind.Unavailable, "directory does not exist: " + request.Dir);
                }
            }

            var closers = new Watch[requests.Length];
            for (var i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                var watch = new MockWatch
                {
                    Path = request.Dir,
                    Callback = request.Callback!,
                    Recursive = request.Recursive,
                    Ignore = request.Ignore,
                };
                Dirs[request.Dir] = watch;
                closers[i] = watch;
            }

            return closers;
        }
    }

    /// <summary>
    /// Go: <c>func (m *MockWatchBackend) SendEvents(events []fswatch.Event)</c>。目录 watch 在事件路径是它的
    /// 子路径（或递归后代）时匹配。
    /// </summary>
    public void SendEvents(Event[] events)
    {
        // Snapshot callbacks under the lock, then invoke outside the lock to avoid deadlock if the callback re-enters the mock.
        // Go: m.mu.Lock() / … / m.mu.Unlock() 之后再回调 —— C# 的 lock 是作用域式的，故这里显式 Monitor。
        var targets = new Dictionary<MockWatch, (WatchCallback Callback, List<Event> Events)>();
        Monitor.Enter(_mu);
        try
        {
            foreach (var e in events)
            {
                foreach (var w in Dirs.Values)
                {
                    if (w.Closed)
                    {
                        continue;
                    }

                    if (w.Ignore is not null && w.Ignore(e.Path))
                    {
                        continue;
                    }

                    if (!PathIsUnder(e.Path, w.Path, w.Recursive, UseCaseSensitiveFileNames))
                    {
                        continue;
                    }

                    if (targets.TryGetValue(w, out var entry))
                    {
                        entry.Events.Add(e);
                    }
                    else
                    {
                        targets[w] = (w.Callback, new List<Event> { e });
                    }
                }
            }
        }
        finally
        {
            Monitor.Exit(_mu);
        }

        foreach (var (callback, list) in targets.Values)
        {
            callback([.. list], null);
        }
    }

    /// <summary>Go: <c>func (m *MockWatchBackend) SendOverflow()</c>。</summary>
    public void SendOverflow()
    {
        var cbs = new List<WatchCallback>();
        lock (_mu)
        {
            foreach (var w in Dirs.Values)
            {
                if (!w.Closed)
                {
                    cbs.Add(w.Callback);
                }
            }
        }

        foreach (var cb in cbs)
        {
            cb(null, FsWatchException.Overflow());
        }
    }

    /// <summary>Go: <c>func (m *MockWatchBackend) SendChangedPaths(changes []fsbaselineutil.FileChange)</c>。</summary>
    public void SendChangedPaths(List<FileChange>? changes)
    {
        if (changes is null)
        {
            return;
        }

        var events = new List<Event>(changes.Count * 2);
        var seenDirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in changes)
        {
            var kind = EventKind.Update;
            if (c.Deleted)
            {
                kind = EventKind.Delete;
            }

            events.Add(new Event(kind, c.Path));

            // Emit update events for parent directories of changed files.
            // Real filesystem watchers deliver events to non-recursive watches
            // when a child directory is created, which the mock must replicate.
            var dir = GetDirectoryPath(c.Path);
            while (dir.Length != 0 && dir != "/" && dir != ".")
            {
                if (!seenDirs.Add(dir))
                {
                    break;
                }

                events.Add(new Event(EventKind.Update, dir));
                var parent = GetDirectoryPath(dir);
                if (parent == dir)
                {
                    break;
                }

                dir = parent;
            }
        }

        SendEvents([.. events]);
    }

    /// <summary>Go: <c>func pathIsUnder(eventPath, dir string, recursive, useCaseSensitiveFileNames bool) bool</c>。</summary>
    private static bool PathIsUnder(string eventPath, string dir, bool recursive, bool useCaseSensitiveFileNames)
    {
        if (!useCaseSensitiveFileNames)
        {
            eventPath = GetCanonicalFileName(eventPath, false);
            dir = GetCanonicalFileName(dir, false);
        }

        if (!eventPath.StartsWith(dir, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = eventPath[dir.Length..];
        if (rest.Length == 0)
        {
            return false; // exact match = the dir itself, not a child
        }

        if (rest[0] != '/')
        {
            return false; // e.g. dir="/foo", path="/foobar"
        }

        if (!recursive)
        {
            // Direct child only: no further '/' after the separator.
            return !rest[1..].Contains('/', StringComparison.Ordinal);
        }

        return true;
    }

    /// <summary>Go: <c>func (m *MockWatchBackend) WatchState() string</c>。</summary>
    public string WatchState()
    {
        lock (_mu)
        {
            var b = new StringBuilder();
            b.Append("Watch Registrations::\n");

            // Directory watches, sorted by path.
            var dirs = new List<string>();
            foreach (var (dir, w) in Dirs)
            {
                if (!w.Closed)
                {
                    dirs.Add(dir);
                }
            }

            dirs.Sort(StringComparer.Ordinal);

            b.Append("Directory watches::\n");
            if (dirs.Count == 0)
            {
                b.Append("  (none)\n");
            }

            foreach (var d in dirs)
            {
                var w = Dirs[d];
                if (w.Recursive)
                {
                    b.Append($"  {d} (recursive)\n");
                }
                else
                {
                    b.Append($"  {d}\n");
                }
            }

            return b.ToString();
        }
    }
}

/// <summary>Go: <c>type MockWatch struct</c>。记录单个注册的 watch。</summary>
public sealed class MockWatch : Watch
{
    public string Path = "";
    public WatchCallback Callback = null!;
    public bool Recursive;
    public Func<string, bool>? Ignore;
    public bool Closed;

    /// <summary>Go: <c>func (w *MockWatch) Close() error</c>。</summary>
    public void Close() => Closed = true;
}
