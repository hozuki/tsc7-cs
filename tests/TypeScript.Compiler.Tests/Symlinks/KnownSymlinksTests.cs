// ported from: internal/symlinks/knownsymlinks_test.go
//
// 说明：
// - Go 的 `TestKnownSymlinksThreadSafety` 用 10 个 goroutine 并发写；C# 侧用 `Parallel.For` 复刻同样的
//   并发形状（每个 id 一个互不相同的软链），断言也照 Go 保留。
// - Go 的 `t.Parallel()` 没有对应物（xunit 默认并发跑不同测试类），不移植。
// - Go 的 `string(rune(id))` → C# 的 `((char)id).ToString()`（Go 的 rune 就是码点，这里 id < 10）。
// - Go 的 `(value, ok)` 两返回值 → C# `SyncMap.Load` 返回 `(Value, Ok)` 元组（Collections 的约定）。
using System.Collections.Concurrent;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.Symlinks;
using static TypeScript.Symlinks.Symlinks;
using static TypeScript.TSPath.TSPath;
using Xunit;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class KnownSymlinksTests
{
    [Fact]
    public void TestNewKnownSymlink()
    {
        var cache = NewKnownSymlink("/test/dir", true);
        Assert.NotNull(cache);
        Assert.Equal("/test/dir", cache._cwd);
        Assert.True(cache._useCaseSensitiveFileNames);
    }

    [Fact]
    public void TestSetDirectory()
    {
        var cache = NewKnownSymlink("/test/dir", true);
        var symlinkPath = ToPath("/test/symlink", "/test/dir", true).EnsureTrailingDirectorySeparator();
        var realDirectory = new KnownDirectoryLink
        {
            Real = "/real/path/",
            RealPath = ToPath("/real/path", "/test/dir", true).EnsureTrailingDirectorySeparator(),
        };

        cache.SetDirectory("/test/symlink", symlinkPath, realDirectory);

        // Check that directory was stored
        var (stored, ok) = cache.Directories().Load(symlinkPath);
        Assert.True(ok, "Expected directory to be stored");
        Assert.Equal(realDirectory.Real, stored!.Real);
        Assert.Equal(realDirectory.RealPath, stored.RealPath);

        // Check that realpath mapping was created
        var (set, setOk) = cache.DirectoriesByRealpath().Load(realDirectory.RealPath);
        Assert.True(setOk && set!.Size() != 0, "Expected realpath mapping to be created");
        Assert.True(set.Has("/test/symlink"), "Expected symlink '/test/symlink' to be in set");
    }

    [Fact]
    public void TestSetFile()
    {
        var cache = NewKnownSymlink("/test/dir", true);
        const string symlink = "/test/symlink/file.ts";
        var symlinkPath = ToPath(symlink, "/test/dir", true);
        const string realpath = "/real/path/file.ts";

        cache.SetFile(symlink, symlinkPath, realpath);

        var (stored, ok) = cache.Files().Load(symlinkPath);
        Assert.True(ok, "Expected file to be stored");
        Assert.Equal(realpath, stored);
    }

    [Fact]
    public void TestProcessResolution()
    {
        var cache = NewKnownSymlink("/test/dir", true);

        // Test with empty paths
        cache.ProcessResolution("", "");
        cache.ProcessResolution("original", "");
        cache.ProcessResolution("", "resolved");

        // Test with valid paths
        const string originalPath = "/test/original/file.ts";
        const string resolvedPath = "/test/resolved/file.ts";
        cache.ProcessResolution(originalPath, resolvedPath);

        // Check that file was stored
        var symlinkPath = ToPath(originalPath, "/test/dir", true);
        var (stored, ok) = cache.Files().Load(symlinkPath);
        Assert.True(ok, "Expected file to be stored");
        Assert.Equal(resolvedPath, stored);
    }

    public static TheoryData<string, string, string, string, string> GuessDirectorySymlinkData => new()
    {
        { "identical paths", "/test/path/file.ts", "/test/path/file.ts", "/test/dir", "/|/" },
        { "different files same directory", "/test/path/file1.ts", "/test/path/file2.ts", "/test/dir", "|" },
        { "different directories", "/test/path1/file.ts", "/test/path2/file.ts", "/test/dir", "/test/path1|/test/path2" },
        { "node_modules paths", "/test/node_modules/pkg/file.ts", "/test/node_modules/pkg/file.ts", "/test/dir", "/test/node_modules/pkg|/test/node_modules/pkg" },
        { "scoped package paths", "/test/node_modules/@scope/pkg/file.ts", "/test/node_modules/@scope/pkg/file.ts", "/test/dir", "/test/node_modules/@scope/pkg|/test/node_modules/@scope/pkg" },
    };

    [Theory]
    [MemberData(nameof(GuessDirectorySymlinkData))]
    public void TestGuessDirectorySymlink(string name, string a, string b, string cwd, string expectedJoined)
    {
        _ = name;
        var cache = NewKnownSymlink("/test/dir", true);
        var expected = expectedJoined.Split('|');

        var (commonResolved, commonOriginal) = cache.GuessDirectorySymlink(a, b, cwd);

        Assert.Equal(expected[0], commonResolved);
        Assert.Equal(expected[1], commonOriginal);
    }

    public static TheoryData<string, string, bool> IsNodeModulesOrScopedPackageDirectoryData => new()
    {
        { "node_modules", "node_modules", true },
        { "scoped package", "@scope", true },
        { "regular directory", "src", false },
        { "empty string", "", false },
        { "case insensitive node_modules", "NODE_MODULES", false }, // The function is case sensitive
        { "case insensitive scoped", "@SCOPE", true },
    };

    [Theory]
    [MemberData(nameof(IsNodeModulesOrScopedPackageDirectoryData))]
    public void TestIsNodeModulesOrScopedPackageDirectory(string name, string dir, bool expected)
    {
        _ = name;
        var cache = NewKnownSymlink("/test/dir", true);
        Assert.Equal(expected, cache.IsNodeModulesOrScopedPackageDirectory(dir));
    }

    [Fact]
    public void TestSetSymlinksFromResolutions()
    {
        var cache = NewKnownSymlink("/test/dir", true);

        // Mock resolution data
        var resolvedModules = new (string OriginalPath, string ResolvedPath, string ModuleName, ModuleKind Mode, Path FilePath)[]
        {
            ("/test/original/file1.ts", "/test/resolved/file1.ts", "module1", ModuleKind.None, ToPath("/test/source.ts", "/test/dir", true)),
            ("/test/original/file2.ts", "/test/resolved/file2.ts", "module2", ModuleKind.None, ToPath("/test/source.ts", "/test/dir", true)),
        };

        // Mock callbacks
        Action<Action<ResolvedModule, string, ModuleKind, Path>, SourceFile?> forEachResolvedModule = (callback, _) =>
        {
            foreach (var res in resolvedModules)
            {
                var resolution = new ResolvedModule
                {
                    OriginalPath = res.OriginalPath,
                    ResolvedFileName = res.ResolvedPath,
                };
                callback(resolution, res.ModuleName, res.Mode, res.FilePath);
            }
        };

        Action<Action<ResolvedTypeReferenceDirective, string, ModuleKind, Path>, SourceFile?> forEachResolvedTypeReferenceDirective = (_, _) =>
        {
            // No type reference directives for this test
        };

        cache.SetSymlinksFromResolutions(forEachResolvedModule, forEachResolvedTypeReferenceDirective);

        // Check that files were stored
        foreach (var res in resolvedModules)
        {
            var symlinkPath = ToPath(res.OriginalPath, "/test/dir", true);
            var (stored, ok) = cache.Files().Load(symlinkPath);
            Assert.True(ok, $"Expected file '{res.OriginalPath}' to be stored");
            Assert.Equal(res.ResolvedPath, stored);
        }
    }

    [Fact]
    public void TestKnownSymlinksThreadSafety()
    {
        var cache = NewKnownSymlink("/test/dir", true);
        var errors = new ConcurrentBag<string>();

        // Test concurrent access（Go: 10 个 goroutine + channel 等待）
        Parallel.For(0, 10, id =>
        {
            var symlink = "/test/symlink" + (char)id;
            var symlinkPath = ToPath(symlink, "/test/dir", true).EnsureTrailingDirectorySeparator();
            var realDirectory = new KnownDirectoryLink
            {
                Real = "/real/path" + (char)id + "/",
                RealPath = ToPath("/real/path" + (char)id, "/test/dir", true).EnsureTrailingDirectorySeparator(),
            };

            cache.SetDirectory(symlink, symlinkPath, realDirectory);

            // Read back
            var (stored, ok) = cache.Directories().Load(symlinkPath);
            if (!ok)
            {
                errors.Add($"Task {id}: Expected directory to be stored");
                return;
            }

            if (stored!.Real != realDirectory.Real)
            {
                errors.Add($"Task {id}: Expected Real to be '{realDirectory.Real}', got '{stored.Real}'");
            }
        });

        Assert.Empty(errors);

        // Verify all directories were stored
        Assert.Equal(10, cache.Directories().Size());
    }
}
