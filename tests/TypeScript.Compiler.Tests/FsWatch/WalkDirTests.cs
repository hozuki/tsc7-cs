// ported from: internal/fswatch/walkdir_test.go
//
// 说明：
// - Go 用 `runWalkDirTest` 让每个用例对两个实现（native `walkDir` / `walkDirGeneric`）各跑一遍 → C# 用
//   `[Theory] + [InlineData(false)]（native）/ [InlineData(true)]（generic）`，与 Go 的 "native"/"generic" 子测试一一对应。
// - Go 的回调是 `func(path string, isDir bool) error`，返回错误即中止 → C# 的 `WalkDirFunc` 里用**抛异常**中止；
//   `errors.Is(err, sentinel)` → `Assert.Same(sentinel, ex)`（同一实例）。
// - Go 的 `t.Skip`（Windows 不强制 POSIX 权限位；root 用户）→ xunit 2.x 没有运行时 Skip，用**提前返回**代替，
//   并在注释里标出对应的 Go `t.Skip`。`makeDirSymlink` 在 Windows 用 `mklink /J`（junction，不需要管理员），
//   两者都失败时同样提前返回（Go 的 `t.Skipf("directory symlink support is not available")`）。
// - Go 的 `newTmpDir` 会 `filepath.EvalSymlinks` 解析临时目录里的符号链接（macOS 的 /var → /private/var）→
//   C# 用已移植的 `TypeScript.NativePath.NativePath.Realpath`，保证与 watcher 报的路径一致。
using TypeScript.FsWatch;

using Xunit;

using static TypeScript.FsWatch.FsWatch;

namespace TypeScript.Tests;

public class WalkDirTests
{
    private static string NewTmpDir() => FsWatchTestSupport.NewTmpDir("fswatch-walk-");

    private static bool TryMakeDirSymlink(string target, string link) => FsWatchTestSupport.TryMakeDirSymlink(target, link);

    private static void Walk(bool generic, string dir, bool recursive, WalkDirFunc? fn)
    {
        if (generic)
        {
            WalkDirGeneric(dir, recursive, fn);
        }
        else
        {
            WalkDir(dir, recursive, fn);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirDoesNotFollowSymlinkedDir(bool generic)
    {
        var root = NewTmpDir();
        var target = Path.Combine(NewTmpDir(), "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "child"), "hidden");

        var link = Path.Combine(root, "link");
        if (!TryMakeDirSymlink(target, link))
        {
            return; // Go: t.Skipf
        }

        var found = new Dictionary<string, bool>();
        Walk(generic, root, true, (path, isDir) => found[path] = isDir);

        Assert.True(found.ContainsKey(link), $"symlink {link} missing from walk");
        Assert.False(found[link], $"symlink {link} was treated as a directory");
        Assert.False(found.ContainsKey(Path.Combine(link, "child")), "walkDir followed symlinked directory");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirDoesNotFollowRootSymlinkedDir(bool generic)
    {
        var root = NewTmpDir();
        var target = Path.Combine(NewTmpDir(), "target");
        Directory.CreateDirectory(target);

        var link = Path.Combine(root, "link");
        if (!TryMakeDirSymlink(target, link))
        {
            return; // Go: t.Skipf
        }

        Assert.ThrowsAny<Exception>(() => Walk(generic, link, true, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirIgnoresUnreadableSubdir(bool generic)
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Go: t.Skip("Windows does not enforce POSIX directory permission bits")
        }

        var root = NewTmpDir();
        var denied = Path.Combine(root, "denied");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(denied, "child"), "hidden");

        try
        {
            File.SetUnixFileMode(denied, UnixFileMode.None);
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            var found = new Dictionary<string, bool>();
            Walk(generic, root, true, (path, isDir) => found[path] = isDir);

            Assert.False(found.ContainsKey(denied), $"unreadable directory should be ignored, found {denied}");
            Assert.False(found.ContainsKey(Path.Combine(denied, "child")), "unreadable child should be ignored");
        }
        finally
        {
            File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirMissingDir(bool generic)
    {
        var dir = Path.Combine(NewTmpDir(), "nonexistent");
        Assert.ThrowsAny<Exception>(() => Walk(generic, dir, true, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirNotADir(bool generic)
    {
        var f = Path.Combine(NewTmpDir(), "file");
        File.WriteAllText(f, "x");
        Assert.ThrowsAny<Exception>(() => Walk(generic, f, true, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirEntries(bool generic)
    {
        var root = NewTmpDir();
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "b.txt"), "b");

        var found = new Dictionary<string, bool>();
        Walk(generic, root, true, (path, isDir) => found[path] = isDir);

        Assert.True(found.ContainsKey(Path.Combine(root, "a.txt")), "missing a.txt");
        Assert.True(found.ContainsKey(sub), "missing sub/");
        Assert.True(found.ContainsKey(Path.Combine(sub, "b.txt")), "missing sub/b.txt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirCallback(bool generic)
    {
        var root = NewTmpDir();
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "f.txt"), "f");

        var dirs = new List<string>();
        var files = new List<string>();
        Walk(generic, root, true, (path, isDir) =>
        {
            if (isDir)
            {
                dirs.Add(path);
            }
            else
            {
                files.Add(path);
            }
        });

        Assert.True(dirs.Count >= 2, $"expected at least 2 dirs (root + sub), got {dirs.Count}");
        Assert.True(files.Count >= 1, $"expected at least 1 file, got {files.Count}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestWalkDirCallbackError(bool generic)
    {
        var root = NewTmpDir();
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");

        var sentinel = new InvalidOperationException("stop");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Walk(generic, root, true, (_, _) => throw sentinel));

        Assert.Same(sentinel, ex);
    }
}
