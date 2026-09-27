// ported from: internal/ls/autoimport/util_test.go
//
// 与 Go 测试的差异（逐条）：
// - Go 的表驱动 + `t.Run(tt.input, …)` → xunit 的 `[Theory]`/`[InlineData]`（失败信息里带输入字符串）。
// - Go 的 `reflect.DeepEqual(actualWords, tt.expectedWords)` → `Assert.Equal(expected, actual)`（逐元素、按顺序）。
// - `wordIndices` 是未导出函数 → C# 的 `internal` `AutoImport.WordIndices`，测试程序集靠 InternalsVisibleTo 访问。
// - Go 的 `vfstest.FromMap(map[string]any{…}, true)` → `FromMap(new Dictionary<string, object> { … }, true)`
//   （`VfsTest.Symlink` 造符号链接项）。
using TypeScript.LsAutoImport;
using TypeScript.Vfs;
using Xunit;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class LsAutoImportUtilTests
{
    [Theory]
    // Basic camelCase
    [InlineData("camelCase", new[] { "camelCase", "Case" })]
    // snake_case
    [InlineData("snake_case", new[] { "snake_case", "case" })]
    // ParseURL - uppercase sequence followed by lowercase
    [InlineData("ParseURL", new[] { "ParseURL", "URL" })]
    // XMLHttpRequest - multiple uppercase sequences
    [InlineData("XMLHttpRequest", new[] { "XMLHttpRequest", "HttpRequest", "Request" })]
    // Single word lowercase
    [InlineData("hello", new[] { "hello" })]
    // Single word uppercase
    [InlineData("HELLO", new[] { "HELLO" })]
    // Mixed with numbers
    [InlineData("parseHTML5Parser", new[] { "parseHTML5Parser", "HTML5Parser", "Parser" })]
    // Underscore variations
    [InlineData("__proto__", new[] { "__proto__", "proto__" })]
    [InlineData("_private_member", new[] { "_private_member", "member" })]
    // Single character
    [InlineData("a", new[] { "a" })]
    [InlineData("A", new[] { "A" })]
    // Consecutive underscores
    [InlineData("test__double__underscore", new[] { "test__double__underscore", "double__underscore", "underscore" })]
    public void TestWordIndices(string input, string[] expectedWords)
    {
        var indices = AutoImport.WordIndices(input);

        // Convert indices to actual word slices for comparison
        var actualWords = new string[indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            actualWords[i] = input[indices[i]..];
        }

        Assert.Equal(expectedWords, actualWords);
    }

    /// <summary>TestGetPackageRealpathFuncs_FollowsNodeModulesSymlinks：toRealpath 必须跟随包目录之外的符号链接
    /// （例如 node_modules 项）。否则模块解析器会把未解析的符号链接路径当缓存键，同一个文件被加载多次
    /// （pnpm/Bazel 布局下桶文件大量 re-export 时内存爆炸，issue #2780）。</summary>
    [Fact]
    public void GetPackageRealpathFuncsFollowsNodeModulesSymlinks()
    {
        // Simulate a layout where the package directory is itself a symlink (e.g. Bazel's
        // convenience symlinks or pnpm's virtual store):
        //   /symlink-bin/pkg/              -> symlink to /real/bin/pkg/
        //   /real/bin/pkg/node_modules/dep -> symlink to /real/dep/
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/symlink-bin/pkg"] = Symlink("/real/bin/pkg"),
                ["/real/bin/pkg/index.d.ts"] = "export declare const a: number;",
                ["/real/bin/pkg/node_modules/dep"] = Symlink("/real/dep"),
                ["/real/dep/index.d.ts"] = "export declare const b: number;",
                ["/real/dep/src/utils/helper.d.ts"] = "export declare const c: number;",
            },
            true /*useCaseSensitiveFileNames*/);

        var (toRealpath, _) = AutoImport.GetPackageRealpathFuncs(fs, "/symlink-bin/pkg");

        // Files inside the package should be converted via string replacement (fast path).
        Assert.Equal("/real/bin/pkg/index.d.ts", toRealpath("/symlink-bin/pkg/index.d.ts"));

        // Files outside the package (e.g. node_modules symlinks) should be resolved via
        // fs.Realpath so the cache key is the canonical realpath, not the symlink path.
        Assert.Equal("/real/dep/index.d.ts", toRealpath("/real/bin/pkg/node_modules/dep/index.d.ts"));

        // Files in subdirectories of an already-resolved external package should
        // use the cached prefix mapping without additional realpath calls.
        Assert.Equal("/real/dep/src/utils/helper.d.ts", toRealpath("/real/bin/pkg/node_modules/dep/src/utils/helper.d.ts"));
    }

    /// <summary>TestGetPackageRealpathFuncs_DuplicateCacheKeys：两个包各自有指向同一个依赖的 node_modules 符号链接时，
    /// 两条路径必须归一到同一个 realpath，避免重复加载。</summary>
    [Fact]
    public void GetPackageRealpathFuncsAvoidsDuplicateCacheKeys()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/workspace/packages/app-a"] = Symlink("/store/app-a"),
                ["/workspace/packages/app-b"] = Symlink("/store/app-b"),
                ["/store/app-a/index.d.ts"] = "export declare const a: number;",
                ["/store/app-b/index.d.ts"] = "export declare const b: number;",
                ["/store/app-a/node_modules/shared-lib"] = Symlink("/store/shared-lib"),
                ["/store/app-b/node_modules/shared-lib"] = Symlink("/store/shared-lib"),
                ["/store/shared-lib/index.d.ts"] = "export declare const shared: string;",
            },
            true /*useCaseSensitiveFileNames*/);

        var (toRealpathA, _) = AutoImport.GetPackageRealpathFuncs(fs, "/workspace/packages/app-a");
        var (toRealpathB, _) = AutoImport.GetPackageRealpathFuncs(fs, "/workspace/packages/app-b");

        const string expectedRealpath = "/store/shared-lib/index.d.ts";
        Assert.Equal(expectedRealpath, toRealpathA("/store/app-a/node_modules/shared-lib/index.d.ts"));
        Assert.Equal(expectedRealpath, toRealpathB("/store/app-b/node_modules/shared-lib/index.d.ts"));
    }

    /// <summary>TestGetPackageRealpathFuncs_NonSymlinkedPackageWithSymlinkedDeps：包目录本身不是符号链接时，
    /// toRealpath 仍要能跟随包外（经 node_modules 符号链接到达的）依赖。</summary>
    [Fact]
    public void GetPackageRealpathFuncsNonSymlinkedPackageWithSymlinkedDeps()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/real/my-pkg/index.d.ts"] = "export declare const a: number;",
                ["/real/my-pkg/node_modules/dep"] = Symlink("/real/dep"),
                ["/real/dep/index.d.ts"] = "export declare const b: number;",
            },
            true /*useCaseSensitiveFileNames*/);

        var (toRealpath, _) = AutoImport.GetPackageRealpathFuncs(fs, "/real/my-pkg");

        // Files inside the (non-symlinked) package should be returned unchanged.
        Assert.Equal("/real/my-pkg/index.d.ts", toRealpath("/real/my-pkg/index.d.ts"));

        // Files outside the package reached via symlinked node_modules should still be resolved.
        Assert.Equal("/real/dep/index.d.ts", toRealpath("/real/my-pkg/node_modules/dep/index.d.ts"));
    }

    /// <summary>本次移植新增：包目录**不是**符号链接时 `toSymlink` 必须是 Go 的 `core.Identity`
    /// （`getPackageRealpathFuncs` 的第三条 return 分支）。</summary>
    [Fact]
    public void GetPackageRealpathFuncsReturnsIdentityToSymlinkWhenNotSymlinked()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/real/my-pkg/index.d.ts"] = "export declare const a: number;",
            },
            true /*useCaseSensitiveFileNames*/);

        var (_, toSymlink) = AutoImport.GetPackageRealpathFuncs(fs, "/real/my-pkg");

        Assert.Equal("/anything/at/all.d.ts", toSymlink("/anything/at/all.d.ts"));
    }

    /// <summary>本次移植新增：包目录是符号链接时 `toSymlink` 要把 realpath 前缀换回符号链接前缀
    /// （Go 的 `strings.CutPrefix(fileName, realPackageDir)` 分支），包外路径原样返回。</summary>
    [Fact]
    public void GetPackageRealpathFuncsReversesPackagePrefixForToSymlink()
    {
        var fs = FromMap(
            new Dictionary<string, object>
            {
                ["/symlink-bin/pkg"] = Symlink("/real/bin/pkg"),
                ["/real/bin/pkg/index.d.ts"] = "export declare const a: number;",
            },
            true /*useCaseSensitiveFileNames*/);

        var (_, toSymlink) = AutoImport.GetPackageRealpathFuncs(fs, "/symlink-bin/pkg");

        Assert.Equal("/symlink-bin/pkg/index.d.ts", toSymlink("/real/bin/pkg/index.d.ts"));
        Assert.Equal("/outside/file.d.ts", toSymlink("/outside/file.d.ts"));
    }
}
