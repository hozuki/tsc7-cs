// ported from: internal/fswatch/fallback_test.go
//
// 说明：
// - Go 的 `fakeFallbackWatcher`/`fakeFallbackWatch` 直接实现 `Watcher`/`Watch` 接口（Go 的接口带未导出方法，
//   但**同一个 package 内**的测试可以满足它）→ C# 的 `Watcher`/`Watch` 是公开接口，测试程序集直接实现即可。
// - Go 的 `fmt.Errorf("%s: %w", w.name, w.failWith)` → C# 的 `new InvalidOperationException($"{name}: …", failWith)`
//   （包装异常 + InnerException；`errors.Is(err, sentinel)` 对应 `FsWatchException.Is(err, kind)`，沿 InnerException 链找）。
// - `t.Cleanup(func(){ watch.Close() })` → `try/finally`。
using TypeScript.FsWatch;

using Xunit;

using static TypeScript.FsWatch.FsWatch;

namespace TypeScript.Tests;

public class FallbackTests
{
    private sealed class FakeFallbackWatch(FakeFallbackWatcher watcher, string dir) : Watch
    {
        public void Close()
        {
            lock (watcher.Mu)
            {
                watcher.Closed.Add(dir);
            }
        }
    }

    private sealed class FakeFallbackWatcher(string name, Exception? failWith, Func<string, bool>? failDir) : Watcher
    {
        internal readonly object Mu = new();
        internal readonly List<string> Watched = [];
        internal readonly List<string> Closed = [];

        public string Name() => name;

        public bool Available() => true;

        public bool HasFastRecursiveBackend() => false;

        private bool ShouldFail(string dir) => failWith is not null && (failDir is null || failDir(dir));

        public Watch WatchDirectory(string dir, WatchCallback fn, params WatchOption[] opts)
        {
            lock (Mu)
            {
                if (ShouldFail(dir))
                {
                    throw new InvalidOperationException($"{name}: {failWith!.Message}", failWith);
                }

                Watched.Add(dir);
                return new FakeFallbackWatch(this, dir);
            }
        }

        public Watch[] WatchDirectories(WatchDirectoryRequest[] requests)
        {
            lock (Mu)
            {
                foreach (var request in requests)
                {
                    if (ShouldFail(request.Dir))
                    {
                        throw new InvalidOperationException($"{name}: {failWith!.Message}", failWith);
                    }
                }

                var watches = new Watch[requests.Length];
                for (var i = 0; i < requests.Length; i++)
                {
                    Watched.Add(requests[i].Dir);
                    watches[i] = new FakeFallbackWatch(this, requests[i].Dir);
                }

                return watches;
            }
        }

        public Watch WatchFile(string path, WatchCallback fn) => WatchDirectory(path, fn);

        internal string[] WatchedDirs()
        {
            lock (Mu)
            {
                return Watched.OrderBy(d => d, StringComparer.Ordinal).ToArray();
            }
        }

        internal string[] ClosedDirs()
        {
            lock (Mu)
            {
                return Closed.OrderBy(d => d, StringComparer.Ordinal).ToArray();
            }
        }
    }

    [Fact]
    public void TestFallbackWatcherRoutesUnsupportedDirectories()
    {
        var primary = new FakeFallbackWatcher(
            "fanotify",
            FsWatchException.FilesystemUnsupported(),
            dir => dir.StartsWith("/mnt/fuse", StringComparison.Ordinal));
        var secondary = new FakeFallbackWatcher("inotify", null, null);
        var watcher = new FallbackWatcher(primary, secondary);

        var watches = watcher.WatchDirectories(
        [
            new WatchDirectoryRequest { Dir = "/project", Callback = (_, _) => { } },
            new WatchDirectoryRequest { Dir = "/project/src", Callback = (_, _) => { } },
            new WatchDirectoryRequest { Dir = "/mnt/fuse/deps", Callback = (_, _) => { } },
            new WatchDirectoryRequest { Dir = "/mnt/fuse/deps/a", Callback = (_, _) => { } },
        ]);

        try
        {
            Assert.Equal(new[] { "/project", "/project/src" }, primary.WatchedDirs());
            Assert.Equal(new[] { "/mnt/fuse/deps", "/mnt/fuse/deps/a" }, secondary.WatchedDirs());
        }
        finally
        {
            foreach (var watch in watches)
            {
                watch.Close();
            }
        }
    }

    [Fact]
    public void TestFallbackWatcherDoesNotFallbackForUnrelatedError()
    {
        var primary = new FakeFallbackWatcher("fanotify", FsWatchException.Unavailable(), null);
        var secondary = new FakeFallbackWatcher("inotify", null, null);
        var watcher = new FallbackWatcher(primary, secondary);

        var ex = Assert.ThrowsAny<Exception>(() => watcher.WatchDirectory("/project", (_, _) => { }));
        Assert.True(FsWatchException.Is(ex, FsWatchErrorKind.Unavailable));
        Assert.Empty(secondary.WatchedDirs());
    }

    [Fact]
    public void TestFallbackWatcherDoesNotUseSecondaryOnHappyPath()
    {
        var primary = new FakeFallbackWatcher("fanotify", null, null);
        var secondary = new FakeFallbackWatcher("inotify", new InvalidOperationException("secondary should not be used"), null);
        var watcher = new FallbackWatcher(primary, secondary);

        var watches = watcher.WatchDirectories(
        [
            new WatchDirectoryRequest { Dir = "/project", Callback = (_, _) => { } },
            new WatchDirectoryRequest { Dir = "/project/src", Callback = (_, _) => { } },
        ]);

        try
        {
            Assert.Empty(secondary.WatchedDirs());
        }
        finally
        {
            foreach (var watch in watches)
            {
                watch.Close();
            }
        }
    }

    [Fact]
    public void TestFallbackWatcherRollsBackRoutedWatchesOnFailure()
    {
        var primary = new FakeFallbackWatcher(
            "fanotify",
            FsWatchException.FilesystemUnsupported(),
            dir => dir != "/project");
        var secondary = new FakeFallbackWatcher(
            "inotify",
            FsWatchException.Unavailable(),
            dir => dir == "/broken");
        var watcher = new FallbackWatcher(primary, secondary);

        var ex = Assert.ThrowsAny<Exception>(() => watcher.WatchDirectories(
        [
            new WatchDirectoryRequest { Dir = "/project", Callback = (_, _) => { } },
            new WatchDirectoryRequest { Dir = "/mnt/fuse", Callback = (_, _) => { } },
            new WatchDirectoryRequest { Dir = "/broken", Callback = (_, _) => { } },
        ]));

        Assert.True(FsWatchException.Is(ex, FsWatchErrorKind.Unavailable));
        Assert.Equal(new[] { "/project" }, primary.ClosedDirs());
        Assert.Equal(new[] { "/mnt/fuse" }, secondary.ClosedDirs());
    }

    [Fact]
    public void TestFanotifyUsesInternalInotifyFallback()
    {
        var fallback = Assert.IsType<FallbackWatcher>(Fanotify());
        Assert.Same(FanotifyWatcherInstance, fallback.Primary);
        Assert.Same(InotifyWatcherInstance, fallback.Secondary);
    }
}
