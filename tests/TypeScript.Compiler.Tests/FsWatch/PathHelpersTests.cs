// 本文件没有 Go 对照文件：Go 的 `filepath.Clean`/`Dir` 是 stdlib，测试由 stdlib 自带。
// 但本移植**手写**了这两个函数（Go 的算法，见 Watcher.cs 文件头第 5、11 条），所以这里把它们的边界钉住——
// 实测过 `CleanPath("C:\\a\\b")` 曾经丢掉卷名（`\\a\\b`），这类错误会让 Windows 上的所有 watch 都订阅到错路径。
// 用例的期望值直接取自 Go 的 `path/filepath` 在同一算法下的输出（含卷名/UNC/尾分隔符/`.`/`..`）。
using TypeScript.FsWatch;

using Xunit;

using static TypeScript.FsWatch.FsWatch;

namespace TypeScript.Tests;

public class PathHelpersTests
{
    [Fact]
    public void TestCleanPathWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(@"C:\a\c", CleanPath(@"C:\a\b\..\c"));
        Assert.Equal(@"C:\a\b", CleanPath(@"C:\a\\b"));
        Assert.Equal(@"C:\a\b", CleanPath(@"C:\a\b\"));
        Assert.Equal(@"C:\", CleanPath(@"C:\"));
        Assert.Equal(@"C:\a\b", CleanPath(@"C:/a/b"));
        Assert.Equal(@"C:\a\b", CleanPath(@"C:\a\.\b"));
        Assert.Equal(@"C:\b", CleanPath(@"C:\a\..\b"));
        Assert.Equal(@"\\server\share\a\b", CleanPath(@"\\server\share\a\b"));
        Assert.Equal(@"\\server\share\b", CleanPath(@"\\server\share\a\..\b"));
        Assert.Equal(@"a\c", CleanPath(@"a\b\..\c"));
        Assert.Equal(@".", CleanPath(""));
        Assert.Equal(@"C:.", CleanPath(@"C:"));
    }

    [Fact]
    public void TestCleanPathPosix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("/a/c", CleanPath("/a/b/../c"));
        Assert.Equal("/a/b", CleanPath("/a//b"));
        Assert.Equal("/a/b", CleanPath("/a/b/"));
        Assert.Equal("/", CleanPath("/"));
        Assert.Equal("/a/b", CleanPath("/a/./b"));
        Assert.Equal("/b", CleanPath("/a/../b"));
        Assert.Equal("a/c", CleanPath("a/b/../c"));
        Assert.Equal(".", CleanPath(""));
    }

    [Fact]
    public void TestDirPathWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(@"C:\a", DirPath(@"C:\a\b"));
        Assert.Equal(@"C:\", DirPath(@"C:\a"));
        Assert.Equal(@"C:\", DirPath(@"C:\"));
        Assert.Equal(@".", DirPath("a"));
        Assert.Equal(@"C:\a", DirPath(@"C:\a\\b"));
        // Go: vol = `\\server\share`，剩余部分 Clean("\\") = "\\" → vol + dir（与 `C:\a` → `C:\` 同理）。
        Assert.Equal(@"\\server\share\", DirPath(@"\\server\share\a"));
    }

    [Fact]
    public void TestDirPathPosix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("/a", DirPath("/a/b"));
        Assert.Equal("/", DirPath("/a"));
        Assert.Equal("/", DirPath("/"));
        Assert.Equal(".", DirPath("a"));
        Assert.Equal("/a", DirPath("/a//b"));
    }

    [Fact]
    public void TestJoinPathSuffix()
    {
        // Go 的 joinPathSuffix 在 suffix 以分隔符开头时直接 `root + suffix`（**不**把 '/' 换成平台分隔符），
        // 所以 Windows 上会得到混合分隔符——逐字照抄 Go 的行为。
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal(@"C:\root", JoinPathSuffix(@"C:\root", ""));
        Assert.Equal(@"C:\root/child", JoinPathSuffix(@"C:\root", "/child"));
        Assert.Equal($@"C:\root{sep}child", JoinPathSuffix(@"C:\root", "child"));
        Assert.Equal($@"C:\root{sep}child", JoinPathSuffix($@"C:\root{sep}", "/child"));
        Assert.Equal($@"C:\root{sep}child", JoinPathSuffix($@"C:\root{sep}", "child"));
    }

    [Fact]
    public void TestIsDirectChild()
    {
        var root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "parent");
        Assert.True(IsDirectChild(root, Path.Combine(root, "child")));
        Assert.False(IsDirectChild(root, root));
        Assert.False(IsDirectChild(root, Path.Combine(root, "child", "grandchild")));
        Assert.False(IsDirectChild(root, root + "-sibling"));
    }
}
