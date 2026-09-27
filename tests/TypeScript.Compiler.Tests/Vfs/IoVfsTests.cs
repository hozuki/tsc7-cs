// ported from: internal/vfs/iovfs/iofs_test.go
//
// 说明：
// - Go 用 `testing/fstest.MapFS{...}`（键不带前导 '/'、没有显式目录项，目录由 fstest 合成）；C# 没有
//   fstest，等价物是 `VfsTest.ConvertMapFS(map, true, null)`（键同样不带前导 '/'，中间目录在构造时建出）。
//   `null` 时钟 → ConvertMapFS 内部用 SystemClock（与 Go 里 fstest 不关心时间的用例一致）。
// - Go 的 `fs.ReadFile(p)` 返回 (contents, ok) → C# `fs.TryReadFile(p, out var contents)`；
//   失败时 contents == ""（与 Go 的零值一致）。
// - Go 的 `testutil.AssertPanics(..., `vfs: path "bar" is not absolute`)` → `Assert.Throws<DebugFailureException>`；
//   消息由 Debug.Fail 加了 "Debug failure. " 前缀，故用 `Assert.Contains` 匹配 Go 的 panic 文本。
// - `assert.DeepEqual(t, entries.Directories, []string{...})` → `Assert.Equal(string[], string[])`（xunit 逐元素）。
// - 每个 t.Run 子测试 → 独立的 `[Fact]`，名字为 `<GoTest>_<SubTest>`。
using TypeScript;
using TypeScript.Vfs.Iovfs;
using TypeScript.Vfs.VfsTest;
using Xunit;

namespace TypeScript.Tests;

public class IoVfsTests
{
    // Go: testfs := fstest.MapFS{...}; fs := iovfs.From(testfs, true)
    private static FsWithSys CreateTestFs() => Iovfs.From(VfsTest.ConvertMapFS(new Dictionary<string, MapFile>
    {
        ["foo.ts"] = new MapFile { Data = "hello, world" },
        ["dir1/file1.ts"] = new MapFile { Data = "export const foo = 42;" },
        ["dir1/file2.ts"] = new MapFile { Data = "export const foo = 42;" },
        ["dir2/file1.ts"] = new MapFile { Data = "export const foo = 42;" },
    }, true, null), true);

    [Fact]
    public void TestIOFS_ReadFile()
    {
        var fs = CreateTestFs();

        Assert.True(fs.TryReadFile("/foo.ts", out var content));
        Assert.Equal("hello, world", content);

        Assert.False(fs.TryReadFile("/does/not/exist.ts", out content));
        Assert.Equal("", content);
    }

    [Fact]
    public void TestIOFS_ReadFileUnrooted()
    {
        var fs = CreateTestFs();

        var ex = Assert.Throws<DebugFailureException>(() => fs.TryReadFile("bar", out _));
        Assert.Contains("vfs: path \"bar\" is not absolute", ex.Message);
    }

    [Fact]
    public void TestIOFS_FileExists()
    {
        var fs = CreateTestFs();

        Assert.True(fs.FileExists("/foo.ts"));
        Assert.False(fs.FileExists("/bar"));
    }

    [Fact]
    public void TestIOFS_DirectoryExists()
    {
        var fs = CreateTestFs();

        Assert.True(fs.DirectoryExists("/"));
        Assert.True(fs.DirectoryExists("/dir1"));
        Assert.True(fs.DirectoryExists("/dir1/"));
        Assert.True(fs.DirectoryExists("/dir1/./"));
        Assert.False(fs.DirectoryExists("/bar"));
    }

    [Fact]
    public void TestIOFS_GetAccessibleEntries()
    {
        var fs = CreateTestFs();

        var entries = fs.GetAccessibleEntries("/");
        Assert.Equal(["dir1", "dir2"], entries.Directories);
        Assert.Equal(["foo.ts"], entries.Files);
    }

    [Fact]
    public void TestIOFS_Realpath()
    {
        var fs = CreateTestFs();

        var realpath = fs.Realpath("/foo.ts");
        Assert.Equal("/foo.ts", realpath);
    }

    [Fact]
    public void TestIOFS_UseCaseSensitiveFileNames()
    {
        var fs = CreateTestFs();

        Assert.True(fs.UseCaseSensitiveFileNames());
    }
}
