// ported from: internal/fswatch/watcher_test.go（TestPathComparer / TestPathComparerUnicodeAlignment /
//              TestPathComparerExactKeys / TestRebasePath / TestIsInDirectoryOrSelf / TestFileCallbackCaseSensitivity）
//
// 说明：
// - Go 的 `pathComparer{ignoreCase: X}` → C# 的 `new VolumePathComparer(X)`（未导出类型按 C# 习惯命名，见 PathCompare.cs）。
//   `suffix(root, path) (string, bool)` → `Suffix(root, path, out var suffix)`；`contains` → `Contains`；
//   `rebase(path, from, to) (string, bool)` → `Rebase(path, from, to, out var result)`。
// - Go 表里 `exact`/`ignoreCase` 两列分别是 ignoreCase=false/true 时的期望；逐行照抄。
// - Go 的 `strings.EqualFold` / `strings.ToUpper` → C# 的 `FsWatch.EqualFold` / `ToUpperInvariant`：Go 的测试与
//   被测代码**都**用 strings.EqualFold，本移植照此对应（映射理由见 PathCompare.cs 的文件头第 4 条与 EqualFold 注释；
//   .NET 的 OrdinalIgnoreCase 不等价于 EqualFold，实测见该注释）。
// - `newDirectWatcher` 用一条独立的 debounce（Go 里每个测试各自 newDebounce，避免共享 goroutine 状态）；
//   xunit 每个测试方法一个新实例，Dispose 里销毁 debounce。
using TypeScript.FsWatch;

using Xunit;

using static TypeScript.FsWatch.FsWatch;

namespace TypeScript.Tests;

public class PathComparerTests : IDisposable
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

    /// <summary>Go: <c>newDirectWatcher(t, dir)</c>。单独测树/debounce 辅助逻辑，绕过后端订阅路径。</summary>
    private DirWatch NewDirectWatcher(string dir)
    {
        var dw = new DirWatch(dir, dir, new Debounce()) { _recursive = true };
        _dirWatches.Add(dw);
        return dw;
    }

    [Fact]
    public void TestPathComparer()
    {
        (string Root, string Path, string Suffix, bool Exact, bool IgnoreCase)[] tests =
        [
            ("/root", "/root", "", true, true),
            ("/root", "/root/file.ts", "/file.ts", true, true),
            ("/root", "/ROOT", "", false, true),
            ("/root", "/ROOT/File.ts", "/File.ts", false, true),
            ("/root", "/ROOT/Nested/File.ts", "/Nested/File.ts", false, true),
            ("/root", "/ROOT2/File.ts", "", false, false),
            ("/root", "/roo", "", false, false),
            ("/root/sub", "/ROOT", "", false, false),
            ("/root", "/other/File.ts", "", false, false),
            ("/root", "/ROOTish/File.ts", "", false, false),
            ("/root/sub", "/ROOT/SUB", "", false, true),
            ("/root/sub", "/ROOT/su", "", false, false),
            ("/root/[", "/ROOT/{/File.ts", "", false, false),
            ("/root/@", "/ROOT/`/File.ts", "", false, false),
            ("/", "/File.ts", "File.ts", true, true),
            ("/", "/", "", true, true),
            ("", "", "", false, false),
            ("", "/File.ts", "", false, false),
            ("/caf\u00e9", "/CAF\u00c9/File.ts", "/File.ts", false, true),
            ("/s", "/\u017f/File.ts", "/File.ts", false, true),
            ("/\u017f", "/S/File.ts", "/File.ts", false, true),
            ("/s/sub", "/\u017f/SUB/File.ts", "/File.ts", false, true),
            ("/\u017f/sub", "/S/SUB/File.ts", "/File.ts", false, true),
            ("/s", "/\u017foo/File.ts", "", false, false),
            ("/k", "/\u212a/File.ts", "/File.ts", false, true),
            ("/\u03c3", "/\u03c2/File.ts", "/File.ts", false, true),
            ("/\u00e9", "/\u00c8/File.ts", "", false, false),
            ("/\u00df", "/SS/File.ts", "/File.ts", false, NativePathFolding),
            ("/root/s", "/ROOT/\u017f/File.ts", "/File.ts", false, true),
            ("/root/\u017f", "/ROOT/S", "", false, true),
        ];

        foreach (var tt in tests)
        {
            foreach (var ignoreCase in new[] { false, true })
            {
                var comparer = new VolumePathComparer(ignoreCase);
                var want = ignoreCase ? tt.IgnoreCase : tt.Exact;

                var ok = comparer.Suffix(tt.Root, tt.Path, out var suffix);
                Assert.True(ok == want && (!ok || suffix == tt.Suffix),
                    $"suffix(\"{tt.Root}\", \"{tt.Path}\"), ignoreCase={ignoreCase}: got (\"{suffix}\", {ok}), want (\"{tt.Suffix}\", {want})");

                Assert.True(comparer.Contains(tt.Root, tt.Path) == want,
                    $"contains(\"{tt.Root}\", \"{tt.Path}\"), ignoreCase={ignoreCase}: want {want}");

                foreach (var to in new[] { "/display", "/" })
                {
                    var rebasedOk = comparer.Rebase(tt.Path, tt.Root, to, out var rebased);
                    Assert.True(rebasedOk == want && (!rebasedOk || rebased == JoinPathSuffix(to, tt.Suffix)),
                        $"rebase(\"{tt.Path}\", \"{tt.Root}\", \"{to}\"), ignoreCase={ignoreCase}: got (\"{rebased}\", {rebasedOk})");
                }
            }
        }
    }

    [Fact]
    public void TestPathComparerUnicodeAlignment()
    {
        string[] parts = ["s", "S", "\u017f", "k", "K", "\u212a", "\u03c3", "\u03c2", "\u00e9", "\u00c9", "\u00c8", "\U00010400", "\U00010428", "\u00ff", "\u00fe", "\u00c3"];
        var comparer = new VolumePathComparer(true);
        for (var padding = 0; padding < 16; padding++)
        {
            var prefix = "/" + new string('a', padding);
            foreach (var a in parts)
            {
                foreach (var b in parts)
                {
                    foreach (var child in new[] { "", "/child" })
                    {
                        var root = prefix + a + child;
                        var path = prefix + b + child.ToUpperInvariant() + "/File.ts";
                        var want = EqualFold(a, b);
                        var ok = comparer.Suffix(root, path, out var suffix);
                        Assert.True(ok == want && (!ok || suffix == "/File.ts"),
                            $"suffix(\"{root}\", \"{path}\"): got (\"{suffix}\", {ok}), want match={want}");
                    }
                }
            }
        }
    }

    [Fact]
    public void TestFileCallbackCaseSensitivity()
    {
        foreach (var ignoreCase in new[] { false, true })
        {
            var got = new List<Event>();
            var dw = NewDirectWatcher("/root");
            dw.SetComparer(new VolumePathComparer(ignoreCase));
            dw.AddCallback("/root", "/root", false, (events, _) =>
            {
                if (events is not null)
                {
                    got.AddRange(events);
                }
            }, null, "/root/file.ts");

            dw._events.Update("/root/FILE.ts");
            dw._events.Update("/root/other.ts");
            dw.TriggerCallbacks();

            if (ignoreCase)
            {
                Assert.Single(got);
                Assert.Equal("/root/file.ts", got[0].Path);
            }
            else
            {
                Assert.Empty(got);
            }
        }
    }

    [Fact]
    public void TestPathComparerExactKeys()
    {
        var c = default(PathComparer);
        foreach (var path in new[] { "/A/File.ts", "/stra\u00dfe/\u0130.ts", "/cafe\u0301.ts", "/bad\u00ff.ts" })
        {
            Assert.Equal(path, c.Key(path));
        }

        Assert.False(c.Rebase("/A/file.ts", "/a", "/target", out _));
        Assert.True(c.Rebase("/a/file.ts", "/a", "/target", out var rebased));
        Assert.Equal("/target/file.ts", rebased);
    }

    [Fact]
    public void TestRebasePath()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var from = Path.Combine(root, "from");
        var to = Path.Combine(root, "to");

        Assert.Equal(to, RebasePath(from, from, to));
        Assert.Equal(Path.Combine(to, "child"), RebasePath(Path.Combine(from, "child"), from, to));
        Assert.Equal(Path.Combine(root, "from-sibling", "child"), RebasePath(Path.Combine(root, "from-sibling", "child"), from, to));
        Assert.Equal(Path.Combine(to, "child"), RebasePath(Path.Combine(root, "child"), root, to));
        Assert.Equal(Path.Combine(root, "child"), RebasePath(Path.Combine(from, "child"), from, root));
    }

    [Fact]
    public void TestIsInDirectoryOrSelf()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var parent = Path.Combine(root, "parent");
        var child = Path.Combine(parent, "child");
        var nested = Path.Combine(child, "nested");
        var siblingPrefix = Path.Combine(root, "parent-sibling");

        Assert.True(IsInDirectoryOrSelf(parent, parent));
        Assert.True(IsInDirectoryOrSelf(parent, child));
        Assert.True(IsInDirectoryOrSelf(parent, nested));
        Assert.False(IsInDirectoryOrSelf(parent, siblingPrefix));
        Assert.True(IsInDirectoryOrSelf(root, root));
        Assert.True(IsInDirectoryOrSelf(root, Path.Combine(root, "child")));
        Assert.False(IsInDirectoryOrSelf("", child));
    }

    [Fact]
    public void TestPhysicalDirForResolvesSymlinkAncestor()
    {
        var root = Directory.CreateTempSubdirectory("fswatch-physical-").FullName;
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(Path.Combine(target, "nested"));

        var link = Path.Combine(root, "link");
        if (!FsWatchTestSupport.TryMakeDirSymlink(target, link))
        {
            return; // Go: t.Skipf（并把符号链接测试留给开发机跑）
        }

        var dir = Path.Combine(link, "nested");
        var want = PhysicalDirFor(Path.Combine(target, "nested"));
        Assert.Equal(want, PhysicalDirFor(dir));
    }
}
