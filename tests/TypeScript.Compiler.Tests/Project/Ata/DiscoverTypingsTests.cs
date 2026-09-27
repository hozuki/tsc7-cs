// ported from: internal/project/ata/discovertypings_test.go
//
// 说明（与 Go 测试的差异，逐条）：
// - Go 的 `t.Run(...)` 每个子测试落成一个 `[Fact]`；`t.Parallel()` 省略（xunit 的并行是集合级）。
// - Go 的 `assert.DeepEqual(t, collections.NewSetFromItems(got...), collections.NewSetFromItems(want...))` →
//   本文件的 `AssertSetEquals`（`HashSet.SetEquals`；Go 的 NewSetFromItems 是集合语义，顺序无关）。
// - Go 的 `assert.Assert(t, cachedTypingPaths == nil)` → `Assert.Empty(cachedTypingPaths)`：C# 侧 nil 切片与空切片
//   无法区分（见 DiscoverTypings.cs 文件头第 2 条），一律是非 null 数组。
// - Go 的 `assert.DeepEqual(t, filesToWatch, []string{…})` → `Assert.Equal(new[]{…}, filesToWatch)`（顺序敏感，
//   C# 侧用插入序，单搜索目录时与 Go 的追加顺序一致）。
// - Go 的 `vfstest.FromMap(files, false)` → `VfsTest.FromMap(files, false)`（`using static`）。
// - Go 的 `projecttestutil.TypesRegistryConfig()`/`TestTypingsLocation` 是别的切片（`internal/testutil/projecttestutil`）
//   里的东西 → 用 AtaTests.cs 里的最小替身 `AtaTestTypesRegistry.Config()`/`AtaTestConstants.TestTypingsLocation`。
// - Go 的 `core.VersionMajorMinor()` → `VersionMajorMinor()`（`using static TypeScript.Core.Core`）。
// - `semver.MustParse` 是 **`SemVer` 静态类**的成员（不在 `Version` 结构上）→ `MustParse(...)`；
//   类型 `Version` 用别名 `SemVerVersion`（避免与 `Core.Version()` 方法歧义）。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Project.Ata;
using TypeScript.Project.Logging;
using TypeScript.Vfs;
using Xunit;
using static TypeScript.Core.Core;
using static TypeScript.SemVer.SemVer;
using static TypeScript.Collections.Collections;
using static TypeScript.Project.Logging.Logging;
using static TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class DiscoverTypingsTests
{
    [Fact]
    public void ShouldUseMappingsFromSafeList()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
            ["/home/src/projects/project/jquery.js"] = "",
            ["/home/src/projects/project/chroma.min.js"] = "",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
            },
            ["/home/src/projects/project/app.js", "/home/src/projects/project/jquery.js", "/home/src/projects/project/chroma.min.js"],
            "/home/src/projects/project",
            new SyncMap<string, CachedTyping>(),
            new Dictionary<string, Dictionary<string, string>>());

        Assert.Empty(cachedTypingPaths);
        AssertSetEquals(["jquery", "chroma-js"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldReturnNodeForCoreModules()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var unresolvedImports = NewSetFromItems("assert", "somename");
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
                UnresolvedImports = unresolvedImports,
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            new SyncMap<string, CachedTyping>(),
            new Dictionary<string, Dictionary<string, string>>());

        Assert.Empty(cachedTypingPaths);
        AssertSetEquals(["node", "somename"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldUseCachedLocations()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
            ["/home/src/projects/project/node.d.ts"] = "",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var cache = new SyncMap<string, CachedTyping>();
        var version = MustParse("1.3.0");
        cache.Store("node", new CachedTyping
        {
            TypingsLocation = "/home/src/projects/project/node.d.ts",
            Version = version,
        });
        var unresolvedImports = NewSetFromItems("fs", "bar");
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
                UnresolvedImports = unresolvedImports,
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            cache,
            new Dictionary<string, Dictionary<string, string>>
            {
                ["node"] = AtaTestTypesRegistry.Config(),
            });

        Assert.Equal(new[] {"/home/src/projects/project/node.d.ts"}, cachedTypingPaths);
        AssertSetEquals(["bar"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldGracefullyHandlePackagesThatHaveBeenRemovedFromTheTypesRegistry()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
            ["/home/src/projects/project/node.d.ts"] = "",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var cache = new SyncMap<string, CachedTyping>();
        var version = MustParse("1.3.0");
        cache.Store("node", new CachedTyping
        {
            TypingsLocation = "/home/src/projects/project/node.d.ts",
            Version = version,
        });
        var unresolvedImports = NewSetFromItems("fs", "bar");
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
                UnresolvedImports = unresolvedImports,
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            cache,
            new Dictionary<string, Dictionary<string, string>>());

        Assert.Empty(cachedTypingPaths);
        AssertSetEquals(["node", "bar"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldSearchOnly2LevelsDeep()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
            ["/home/src/projects/project/node_modules/a/package.json"] = """{ "name": "a" }""",
            ["/home/src/projects/project/node_modules/a/b/package.json"] = """{ "name": "b" }""",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            new SyncMap<string, CachedTyping>(),
            new Dictionary<string, Dictionary<string, string>>());

        Assert.Empty(cachedTypingPaths);
        AssertSetEquals(["a"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldSupportScopedPackages()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
            ["/home/src/projects/project/node_modules/@a/b/package.json"] = """{ "name": "@a/b" }""",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            new SyncMap<string, CachedTyping>(),
            new Dictionary<string, Dictionary<string, string>>());

        Assert.Empty(cachedTypingPaths);
        AssertSetEquals(["@a/b"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldInstallExpiredTypings()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var cache = new SyncMap<string, CachedTyping>();
        var nodeVersion = MustParse("1.3.0");
        var commanderVersion = MustParse("1.0.0");
        cache.Store("node", new CachedTyping
        {
            TypingsLocation = AtaTestConstants.TestTypingsLocation + "/node_modules/@types/node/index.d.ts",
            Version = nodeVersion,
        });
        cache.Store("commander", new CachedTyping
        {
            TypingsLocation = AtaTestConstants.TestTypingsLocation + "/node_modules/@types/commander/index.d.ts",
            Version = commanderVersion,
        });
        var unresolvedImports = NewSetFromItems("http", "commander");
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
                UnresolvedImports = unresolvedImports,
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            cache,
            new Dictionary<string, Dictionary<string, string>>
            {
                ["node"] = AtaTestTypesRegistry.Config(),
                ["commander"] = AtaTestTypesRegistry.Config(),
            });

        Assert.Equal(new[] {"/home/src/Library/Caches/typescript/node_modules/@types/node/index.d.ts"}, cachedTypingPaths);
        AssertSetEquals(["commander"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldInstallExpiredTypingsWithPrereleaseVersionOfTsserver()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var cache = new SyncMap<string, CachedTyping>();
        var nodeVersion = MustParse("1.0.0");
        cache.Store("node", new CachedTyping
        {
            TypingsLocation = AtaTestConstants.TestTypingsLocation + "/node_modules/@types/node/index.d.ts",
            Version = nodeVersion,
        });
        var config = new Dictionary<string, string>(AtaTestTypesRegistry.Config());
        config.Remove("ts" + VersionMajorMinor());

        var unresolvedImports = NewSetFromItems("http");
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
                UnresolvedImports = unresolvedImports,
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            cache,
            new Dictionary<string, Dictionary<string, string>>
            {
                ["node"] = config,
            });

        Assert.Empty(cachedTypingPaths);
        AssertSetEquals(["node"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void PrereleaseTypingsAreProperlyHandled()
    {
        var logger = NewLogTree("DiscoverTypings");
        var files = new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
        };
        var fs = FromMap(files, false /* useCaseSensitiveFileNames */);
        var cache = new SyncMap<string, CachedTyping>();
        var nodeVersion = MustParse("1.3.0-next.0");
        var commanderVersion = MustParse("1.3.0-next.0");
        cache.Store("node", new CachedTyping
        {
            TypingsLocation = AtaTestConstants.TestTypingsLocation + "/node_modules/@types/node/index.d.ts",
            Version = nodeVersion,
        });
        cache.Store("commander", new CachedTyping
        {
            TypingsLocation = AtaTestConstants.TestTypingsLocation + "/node_modules/@types/commander/index.d.ts",
            Version = commanderVersion,
        });
        var config = new Dictionary<string, string>(AtaTestTypesRegistry.Config())
        {
            ["ts" + VersionMajorMinor()] = "1.3.0-next.1",
        };
        var unresolvedImports = NewSetFromItems("http", "commander");
        var (cachedTypingPaths, newTypingNames, filesToWatch) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
                UnresolvedImports = unresolvedImports,
            },
            ["/home/src/projects/project/app.js"],
            "/home/src/projects/project",
            cache,
            new Dictionary<string, Dictionary<string, string>>
            {
                ["node"] = config,
                ["commander"] = AtaTestTypesRegistry.Config(),
            });

        Assert.Empty(cachedTypingPaths);
        AssertSetEquals(["node", "commander"], newTypingNames);
        Assert.Equal(new[] {
                "/home/src/projects/project/bower_components",
                "/home/src/projects/project/node_modules",
            },
            filesToWatch);
    }

    [Fact]
    public void ShouldStripMinAndVersionSuffixesFromFileNames()
    {
        // C#-added（Go 的 discovertypings_test 只覆盖了 ".min" 后缀，见 chroma.min.js 那一例）：
        // `removeMinAndVersionNumbers` 的"版本号后缀"分支（Go 的向后扫描算法）是易错点，这里用公开入口点验证
        // "chai.4.2.0.js" → "chai"、"jquery-min.4.2.3.js" → "jquery"（Go 注释里的第二类例子；注意
        // "angular-route" 并不在 safe list 里，换了 chai）。
        var logger = NewLogTree("DiscoverTypings");
        var fs = FromMap(new Dictionary<string, string>
        {
            ["/home/src/projects/project/app.js"] = "",
            ["/home/src/projects/project/jquery-min.4.2.3.js"] = "",
            ["/home/src/projects/project/chai.4.2.0.js"] = "",
        }, false /* useCaseSensitiveFileNames */);
        var (_, newTypingNames, _) = TypingsInfo.DiscoverTypings(
            fs,
            logger,
            new TypingsInfo
            {
                CompilerOptions = new CompilerOptions(),
                TypeAcquisition = new TypeAcquisition { Enable = Tristate.TSTrue },
            },
            [
                "/home/src/projects/project/app.js",
                "/home/src/projects/project/jquery-min.4.2.3.js",
                "/home/src/projects/project/chai.4.2.0.js",
            ],
            "/home/src/projects/project",
            new SyncMap<string, CachedTyping>(),
            new Dictionary<string, Dictionary<string, string>>());

        AssertSetEquals(["jquery", "chai"], newTypingNames);
    }

    // Go: assert.DeepEqual(collections.NewSetFromItems(got...), collections.NewSetFromItems(want...))（集合语义）
    private static void AssertSetEquals(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        Assert.True(
            new HashSet<string>(expected).SetEquals(actual),
            $"expected set [{string.Join(", ", expected)}] but got [{string.Join(", ", actual)}]");
    }
}
