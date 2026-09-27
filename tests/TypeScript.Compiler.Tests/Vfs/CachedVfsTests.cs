// ported from: internal/vfs/cachedvfs/cachedvfs_test.go
//
// 说明：
// - Go 的 `createMockFS()`（`vfsmock.Wrap(vfstest.FromMap(map[string]string{...}, true))`）→
//   `VfsMock.Wrap(VfsTest.FromMap(new Dictionary<string, string>{...}, true))`（两层都是同一个移植）。
// - Go 的 `cachedvfs.From(underlying)` → `CachedVfs.From(underlying)`；返回类型是 `CachedVfs.FS`（与
//   `TypeScript.Vfs.FS` 接口同名，故用 `var` 承接，避免与本命名空间内的 `FS` 混淆）。
// - `len(underlying.XxxCalls())` → `underlying.XxxCalls().Length`；`UseCaseSensitiveFileNamesCalls()`
//   在 C# 侧是 int 计数，直接比较数值。
//   注：`Assert.Equal(1, collection.Length)` 会触发 xunit 分析器 xUnit2013（项目 TreatWarningsAsErrors=true），
//   故"恰好 1 个调用"的断言写成等价的 `Assert.Single(underlying.XxxCalls())`（数量不为 1 同样失败）。
// - `gotest.tools/assert.Equal(t, want, got)` → xunit 的 `Assert.Equal(expected, actual)`（xunit 惯例）。
// - Go 的 `t.Parallel()` 由 xunit 调度，不逐字搬。
using TypeScript.Vfs.CachedVfs;
using TypeScript.Vfs.VfsMock;
using TypeScript.Vfs.VfsTest;
using Xunit;

namespace TypeScript.Tests;

public class CachedVfsTests
{
    // Go: createMockFS()
    private static FSMock CreateMockFS() => VfsMock.Wrap(VfsTest.FromMap(new Dictionary<string, string>
    {
        ["/some/path/file.txt"] = "hello world",
    }, true));

    [Fact]
    public void TestDirectoryExists()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.DirectoryExists("/some/path");
        Assert.Single(underlying.DirectoryExistsCalls());

        cached.DirectoryExists("/some/path");
        Assert.Single(underlying.DirectoryExistsCalls());

        cached.ClearCache();
        cached.DirectoryExists("/some/path");
        Assert.Equal(2, underlying.DirectoryExistsCalls().Length);

        cached.DirectoryExists("/other/path");
        Assert.Equal(3, underlying.DirectoryExistsCalls().Length);

        cached.DisableAndClearCache();
        cached.DirectoryExists("/some/path");
        Assert.Equal(4, underlying.DirectoryExistsCalls().Length);

        cached.DirectoryExists("/some/path");
        Assert.Equal(5, underlying.DirectoryExistsCalls().Length);

        cached.Enable();
        cached.DirectoryExists("/some/path");
        Assert.Equal(6, underlying.DirectoryExistsCalls().Length);

        cached.DirectoryExists("/some/path");
        Assert.Equal(6, underlying.DirectoryExistsCalls().Length);
    }

    [Fact]
    public void TestFileExists()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.FileExists("/some/path/file.txt");
        Assert.Single(underlying.FileExistsCalls());

        cached.FileExists("/some/path/file.txt");
        Assert.Single(underlying.FileExistsCalls());

        cached.ClearCache();
        cached.FileExists("/some/path/file.txt");
        Assert.Equal(2, underlying.FileExistsCalls().Length);

        cached.FileExists("/other/path/file.txt");
        Assert.Equal(3, underlying.FileExistsCalls().Length);

        cached.DisableAndClearCache();
        cached.FileExists("/some/path/file.txt");
        Assert.Equal(4, underlying.FileExistsCalls().Length);

        cached.FileExists("/some/path/file.txt");
        Assert.Equal(5, underlying.FileExistsCalls().Length);

        cached.Enable();
        cached.FileExists("/some/path/file.txt");
        Assert.Equal(6, underlying.FileExistsCalls().Length);

        cached.FileExists("/some/path/file.txt");
        Assert.Equal(6, underlying.FileExistsCalls().Length);
    }

    [Fact]
    public void TestGetAccessibleEntries()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.GetAccessibleEntries("/some/path");
        Assert.Single(underlying.GetAccessibleEntriesCalls());

        cached.GetAccessibleEntries("/some/path");
        Assert.Single(underlying.GetAccessibleEntriesCalls());

        cached.ClearCache();
        cached.GetAccessibleEntries("/some/path");
        Assert.Equal(2, underlying.GetAccessibleEntriesCalls().Length);

        cached.GetAccessibleEntries("/other/path");
        Assert.Equal(3, underlying.GetAccessibleEntriesCalls().Length);

        cached.DisableAndClearCache();
        cached.GetAccessibleEntries("/some/path");
        Assert.Equal(4, underlying.GetAccessibleEntriesCalls().Length);

        cached.GetAccessibleEntries("/some/path");
        Assert.Equal(5, underlying.GetAccessibleEntriesCalls().Length);

        cached.Enable();
        cached.GetAccessibleEntries("/some/path");
        Assert.Equal(6, underlying.GetAccessibleEntriesCalls().Length);

        cached.GetAccessibleEntries("/some/path");
        Assert.Equal(6, underlying.GetAccessibleEntriesCalls().Length);
    }

    [Fact]
    public void TestRealpath()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.Realpath("/some/path");
        Assert.Single(underlying.RealpathCalls());

        cached.Realpath("/some/path");
        Assert.Single(underlying.RealpathCalls());

        cached.ClearCache();
        cached.Realpath("/some/path");
        Assert.Equal(2, underlying.RealpathCalls().Length);

        cached.Realpath("/other/path");
        Assert.Equal(3, underlying.RealpathCalls().Length);

        cached.DisableAndClearCache();
        cached.Realpath("/some/path");
        Assert.Equal(4, underlying.RealpathCalls().Length);

        cached.Realpath("/some/path");
        Assert.Equal(5, underlying.RealpathCalls().Length);

        cached.Enable();
        cached.Realpath("/some/path");
        Assert.Equal(6, underlying.RealpathCalls().Length);

        cached.Realpath("/some/path");
        Assert.Equal(6, underlying.RealpathCalls().Length);
    }

    [Fact]
    public void TestStat()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.Stat("/some/path");
        Assert.Single(underlying.StatCalls());

        cached.Stat("/some/path");
        Assert.Single(underlying.StatCalls());

        cached.ClearCache();
        cached.Stat("/some/path");
        Assert.Equal(2, underlying.StatCalls().Length);

        cached.Stat("/other/path");
        Assert.Equal(3, underlying.StatCalls().Length);

        cached.DisableAndClearCache();
        cached.Stat("/some/path");
        Assert.Equal(4, underlying.StatCalls().Length);

        cached.Stat("/some/path");
        Assert.Equal(5, underlying.StatCalls().Length);

        cached.Enable();
        cached.Stat("/some/path");
        Assert.Equal(6, underlying.StatCalls().Length);

        cached.Stat("/some/path");
        Assert.Equal(6, underlying.StatCalls().Length);
    }

    [Fact]
    public void TestReadFile()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.TryReadFile("/some/path/file.txt", out _);
        Assert.Single(underlying.ReadFileCalls());

        cached.TryReadFile("/some/path/file.txt", out _);
        Assert.Equal(2, underlying.ReadFileCalls().Length);

        cached.ClearCache();
        cached.TryReadFile("/some/path/file.txt", out _);
        Assert.Equal(3, underlying.ReadFileCalls().Length);

        cached.DisableAndClearCache();
        cached.TryReadFile("/some/path/file.txt", out _);
        Assert.Equal(4, underlying.ReadFileCalls().Length);

        cached.TryReadFile("/some/path/file.txt", out _);
        Assert.Equal(5, underlying.ReadFileCalls().Length);

        cached.Enable();
        cached.TryReadFile("/some/path/file.txt", out _);
        Assert.Equal(6, underlying.ReadFileCalls().Length);

        cached.TryReadFile("/some/path/file.txt", out _);
        Assert.Equal(7, underlying.ReadFileCalls().Length);
    }

    [Fact]
    public void TestUseCaseSensitiveFileNames()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.UseCaseSensitiveFileNames();
        Assert.Equal(1, underlying.UseCaseSensitiveFileNamesCalls());

        cached.UseCaseSensitiveFileNames();
        Assert.Equal(2, underlying.UseCaseSensitiveFileNamesCalls());

        cached.ClearCache();
        cached.UseCaseSensitiveFileNames();
        Assert.Equal(3, underlying.UseCaseSensitiveFileNamesCalls());

        cached.DisableAndClearCache();
        cached.UseCaseSensitiveFileNames();
        Assert.Equal(4, underlying.UseCaseSensitiveFileNamesCalls());

        cached.UseCaseSensitiveFileNames();
        Assert.Equal(5, underlying.UseCaseSensitiveFileNamesCalls());

        cached.Enable();
        cached.UseCaseSensitiveFileNames();
        Assert.Equal(6, underlying.UseCaseSensitiveFileNamesCalls());

        cached.UseCaseSensitiveFileNames();
        Assert.Equal(7, underlying.UseCaseSensitiveFileNamesCalls());
    }

    [Fact]
    public void TestRemove()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.Remove("/some/path/file.txt");
        Assert.Single(underlying.RemoveCalls());

        cached.Remove("/some/path/file.txt");
        Assert.Equal(2, underlying.RemoveCalls().Length);

        cached.ClearCache();
        cached.Remove("/some/path/file.txt");
        Assert.Equal(3, underlying.RemoveCalls().Length);

        cached.DisableAndClearCache();
        cached.Remove("/some/path/file.txt");
        Assert.Equal(4, underlying.RemoveCalls().Length);

        cached.Remove("/some/path/file.txt");
        Assert.Equal(5, underlying.RemoveCalls().Length);

        cached.Enable();
        cached.Remove("/some/path/file.txt");
        Assert.Equal(6, underlying.RemoveCalls().Length);

        cached.Remove("/some/path/file.txt");
        Assert.Equal(7, underlying.RemoveCalls().Length);
    }

    [Fact]
    public void TestWriteFile()
    {
        var underlying = CreateMockFS();
        var cached = CachedVfs.From(underlying);

        cached.WriteFile("/some/path/file.txt", "new content");
        Assert.Single(underlying.WriteFileCalls());

        cached.WriteFile("/some/path/file.txt", "another content");
        Assert.Equal(2, underlying.WriteFileCalls().Length);

        cached.ClearCache();
        cached.WriteFile("/some/path/file.txt", "third content");
        Assert.Equal(3, underlying.WriteFileCalls().Length);

        var call = underlying.WriteFileCalls()[2];
        Assert.Equal("/some/path/file.txt", call.Path);
        Assert.Equal("third content", call.Data);

        cached.DisableAndClearCache();
        cached.WriteFile("/some/path/file.txt", "fourth content");
        Assert.Equal(4, underlying.WriteFileCalls().Length);

        cached.WriteFile("/some/path/file.txt", "fifth content");
        Assert.Equal(5, underlying.WriteFileCalls().Length);

        cached.Enable();
        cached.WriteFile("/some/path/file.txt", "sixth content");
        Assert.Equal(6, underlying.WriteFileCalls().Length);

        cached.WriteFile("/some/path/file.txt", "seventh content");
        Assert.Equal(7, underlying.WriteFileCalls().Length);
    }
}
