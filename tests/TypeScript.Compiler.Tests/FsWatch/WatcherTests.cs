// ported from: internal/fswatch/watcher_test.go（只挑**确定性**的用例：不依赖真实文件系统事件投递的时序）
//
// 说明：
// - Go 的 `runForEachWatcher` 让用例对每个可用后端各跑一遍 → C# 的 `ForEachWatcher`（同一测试里遍历
//   `AllWatchers().Where(Available)`；win32 上只有 "windows"）。
// - 未移植的用例：所有依赖"写文件 → 等事件到达"的用例（TestWatchFileCreate、TestSubscribeDirCreate、
//   TestSubscribeCoalesce*、Timeout/NoGoroutineLeak 等）——它们靠 `time.Sleep` + 事件时序，属于 flaky 范畴。
//   本切片用 `artifacts/fswatch-probe` 的实时冒烟程序覆盖这些行为（create/update/delete/递归/WatchFile/Close）。
// - Go 的 `errors.Is(err, ErrUnavailable)` → `FsWatchException.Is(ex, FsWatchErrorKind.Unavailable)`。
// - `failingBackend` 继承 watcherBase（Go 的匿名嵌入）→ C# 继承 `WatcherBase`。
using TypeScript.FsWatch;

using Xunit;

using static TypeScript.FsWatch.FsWatch;

namespace TypeScript.Tests;

public class WatcherTests : IDisposable
{
    private readonly List<DirWatch> _dirWatches = [];

    public void Dispose()
    {
        foreach (var dw in _dirWatches)
        {
            dw.DestroyDebounce();
        }

        _dirWatches.Clear();
    }

    /// <summary>Go: <c>runForEachWatcher</c>。</summary>
    private static void ForEachWatcher(Action<Watcher> body)
    {
        foreach (var watcher in AllWatchers())
        {
            if (watcher.Available())
            {
                body(watcher);
            }
        }
    }

    private static string NewTmpDir() => FsWatchTestSupport.NewTmpDir("fswatch-api-");

    private DirWatch NewDirectWatcher(string dir)
    {
        var dw = new DirWatch(dir, dir, new Debounce()) { _recursive = true };
        _dirWatches.Add(dw);
        return dw;
    }

    [Fact]
    public void TestSubscribeMissingDirError()
    {
        ForEachWatcher(watcher =>
        {
            var bogus = Path.Combine(NewTmpDir(), "definitely-not-here");
            Assert.ThrowsAny<Exception>(() => watcher.WatchDirectory(bogus, (_, _) => { }));
        });
    }

    [Fact]
    public void TestSubscribeNotADirError()
    {
        ForEachWatcher(watcher =>
        {
            var dir = NewTmpDir();
            var f = Path.Combine(dir, "file.txt");
            File.WriteAllText(f, "x");
            Assert.ThrowsAny<Exception>(() => watcher.WatchDirectory(f, (_, _) => { }));
        });
    }

    [Fact]
    public void TestSubscribeRejectsNilCallback()
    {
        ForEachWatcher(watcher =>
        {
            var ex = Assert.Throws<FsWatchException>(() => watcher.WatchDirectory(NewTmpDir(), null!));
            Assert.Equal(FsWatchErrorKind.NilCallback, ex.Kind);
        });
    }

    [Fact]
    public void TestSubscribeRejectsRelativePath()
    {
        ForEachWatcher(watcher =>
        {
            var ex = Assert.Throws<FsWatchException>(() => watcher.WatchDirectory("relative/path", (_, _) => { }));
            Assert.Equal(FsWatchErrorKind.NotAbsolute, ex.Kind);

            var ex2 = Assert.Throws<FsWatchException>(() => watcher.WatchFile("relative/path/file.txt", (_, _) => { }));
            Assert.Equal(FsWatchErrorKind.NotAbsolute, ex2.Kind);
        });
    }

    [Fact]
    public void TestSubscribeUnsubscribeIdempotent()
    {
        ForEachWatcher(watcher =>
        {
            var dir = NewTmpDir();
            var sub = watcher.WatchDirectory(dir, (_, _) => { });
            sub.Close();
            sub.Close(); // second Close should be a no-op
        });
    }

    [Fact]
    public void TestWatchFileCannotWatchRoot()
    {
        ForEachWatcher(watcher =>
        {
            var root = Path.GetPathRoot(Path.GetTempPath())!;
            var ex = Assert.Throws<FsWatchException>(() => watcher.WatchFile(root, (_, _) => { }));
            Assert.Equal(FsWatchErrorKind.RootPath, ex.Kind);
        });
    }

    [Fact]
    public void TestDefaultBackendMatchesPlatform()
    {
        var d = Default();
        string wantName;
        if (OperatingSystem.IsLinux())
        {
            wantName = Fanotify().Available() ? "fanotify" : "inotify";
        }
        else if (OperatingSystem.IsAndroid())
        {
            wantName = "inotify";
        }
        else if (OperatingSystem.IsMacOS())
        {
            wantName = "fsevents";
        }
        else if (OperatingSystem.IsWindows())
        {
            wantName = "windows";
        }
        else if (OperatingSystem.IsFreeBSD())
        {
            wantName = "kqueue";
        }
        else
        {
            return; // Go: t.Skipf("no expected default watcher for %s")
        }

        Assert.True(d.Available(), $"Default() should be available on this OS");
        Assert.Equal(wantName, d.Name());
    }

    [Fact]
    public void TestUnavailableBackendReturnsError()
    {
        // Pick a watcher that is definitely unavailable on the current OS.
        Watcher? unavailable = null;
        foreach (var w in AllWatchers())
        {
            if (!w.Available())
            {
                unavailable = w;
                break;
            }
        }

        if (unavailable is null)
        {
            return; // Go: t.Skip("all watchers are available on this platform")
        }

        var dir = NewTmpDir();
        var ex = Assert.ThrowsAny<Exception>(() => unavailable.WatchDirectory(dir, (_, _) => { }));
        Assert.True(FsWatchException.Is(ex, FsWatchErrorKind.Unavailable), $"expected Unavailable from {unavailable.Name()}, got {ex.Message}");
    }

    [Fact]
    public void TestBackendRunReturnsStartError()
    {
        var want = new InvalidOperationException("startup failed");
        var b = new FailingBackend(want);
        var ex = Assert.ThrowsAny<Exception>(() => b.Run());
        Assert.Same(want, ex);
    }

    [Fact]
    public void TestDirWatchErrorImplementsError()
    {
        // Go: `var err error = &dirWatchError{err: errors.New("boom")}`（dirWatch 为 nil）。
        var err = new DirWatchError(new InvalidOperationException("boom"), null!);
        Assert.Equal("boom", err.Message);
    }

    [Fact]
    public void TestFileCallbackForwardsErrAlongsideEvents()
    {
        var target = "/abs/dir/target.txt";
        var other = "/abs/dir/sibling.txt";
        var overflow = new InvalidOperationException("overflow");

        var got = new List<(Event[] Events, Exception? Err)>();
        var dw = NewDirectWatcher("/abs/dir");
        dw.AddCallback("/abs/dir", "/abs/dir", false, (events, err) => got.Add((events ?? [], err)), null, target);

        void Cb(Event[]? events, Exception? err)
        {
            foreach (var e in events ?? [])
            {
                if (e.Kind == EventKind.Delete)
                {
                    dw._events.Remove(e.Path);
                }
                else
                {
                    dw._events.Update(e.Path);
                }
            }

            if (err is not null)
            {
                dw._events.SetError(err);
            }

            dw.TriggerCallbacks();
        }

        // Plain events: only target events pass through, sibling dropped.
        Cb([new Event(EventKind.Update, target), new Event(EventKind.Update, other)], null);
        Assert.Single(got);
        Assert.Single(got[0].Events);
        Assert.Equal(target, got[0].Events[0].Path);
        Assert.Null(got[0].Err);

        // Err only, no matching events: still forwarded with empty slice.
        got.Clear();
        Cb([new Event(EventKind.Update, other)], overflow);
        Assert.Single(got);
        Assert.Empty(got[0].Events);
        Assert.Same(overflow, got[0].Err);

        // Err with matching events: deliver both the filtered events and err.
        got.Clear();
        Cb([new Event(EventKind.Delete, target), new Event(EventKind.Update, other)], overflow);
        Assert.Single(got);
        Assert.Single(got[0].Events);
        Assert.Equal(target, got[0].Events[0].Path);
        Assert.Equal(EventKind.Delete, got[0].Events[0].Kind);
        Assert.Same(overflow, got[0].Err);

        // No events, no err: callback not invoked at all.
        got.Clear();
        Cb(null, null);
        Assert.Empty(got);
    }

    /// <summary>
    /// `dirWatch.triggerCallbacks` 的过滤块（非递归只放直接子项；recursive+ignore 按投递路径过滤）。
    /// Go 侧的等价用例（TestNonRecursiveGrandchildIgnored、TestRecursiveWatchWithIgnoreDoesNotFilterByLogicalRoot）
    /// 都跑真实后端，这里改成直接驱动 <see cref="DirWatch"/>，去掉时序依赖（后端侧的行为由 artifacts/fswatch-probe 覆盖）。
    /// </summary>
    [Fact]
    public void TestDirectWatcherCallbackFiltering()
    {
        var nonRecursive = new List<string>();
        var dw = NewDirectWatcher("/root");
        dw.AddCallback("/root", "/root", false, (events, _) =>
        {
            foreach (var e in events ?? [])
            {
                nonRecursive.Add(e.Path);
            }
        }, null, "");

        dw._events.Update("/root/child");
        dw._events.Update("/root/sub/grandchild");
        dw.TriggerCallbacks();
        Assert.Equal(new[] { "/root/child" }, nonRecursive);

        var recursive = new List<string>();
        var dw2 = NewDirectWatcher("/root2");
        dw2.AddCallback("/root2", "/root2", true, (events, _) =>
        {
            foreach (var e in events ?? [])
            {
                recursive.Add(e.Path);
            }
        }, p => p.EndsWith(".skip", StringComparison.Ordinal), "");

        dw2._events.Update("/root2/a.txt");
        dw2._events.Update("/root2/ignored.skip");
        dw2._events.Update("/root2/sub/b.txt");
        dw2.TriggerCallbacks();
        Assert.Equal(new[] { "/root2/a.txt", "/root2/sub/b.txt" }, recursive.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>Go: <c>type failingBackend struct { watcherBase; err error }</c>。</summary>
    private sealed class FailingBackend : WatcherBase
    {
        private readonly Exception _err;

        internal FailingBackend(Exception err)
        {
            _err = err;
            Init(this);
        }

        public override void Start() => throw _err;

        public override void Subscribe(DirWatch w)
        {
        }

        public override void CloseWatch(DirWatch w)
        {
        }
    }
}
