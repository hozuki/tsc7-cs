// ported from: internal/modulespecifiers/specifiers_test.go（345 行）
//
// 说明：
// - Go 的 `mockModuleSpecifierGenerationHost` 里有一个 `ResolveModuleName` 方法，但它**不属于**
//   `ModuleSpecifierGenerationHost` 接口（Go 里也没人通过该接口调用它）→ 不移植（交付报告的"跳过"清单）。
// - Go 的 `TestGetEachFileNameOfModule` / `TestGetEachFileNameOfModuleWithSymlinks` 直接构造 host；
//   C# 用私有嵌套类 `MockModuleSpecifierGenerationHost`（`ModuleSpecifierGenerationHost : OutputPathsHost`
//   所以四个 OutputPaths 成员也要实现）。
// - Go 的表驱动 + t.Run → xunit 的 [Theory]/[MemberData]；断言逐条对应。
// - `containsIgnoredPath`/`tryGetModuleNameFromExportsOrImports` 在 Go 里能被测试直接调用（同 package 的
//   未导出成员）→ C# 侧声明为 `internal`（Properties/AssemblyInfo.cs 已 InternalsVisibleTo 测试程序集）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.ModuleSpecifiers;
using TypeScript.PackageJson;
using TypeScript.Symlinks;
using TypeScript.TsOptions;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.Symlinks.Symlinks;
using Xunit;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class ModuleSpecifiersSpecifiersTests
{
    /// <summary>Go: <c>type mockModuleSpecifierGenerationHost struct</c>。</summary>
    private sealed class MockModuleSpecifierGenerationHost(
        string currentDir,
        bool useCaseSensitiveFileNames,
        KnownSymlinks? symlinkCache) : ModuleSpecifierGenerationHost
    {
        public readonly string CurrentDir = currentDir;
        public readonly bool UseCaseSensitiveFileNamesValue = useCaseSensitiveFileNames;
        public readonly KnownSymlinks? SymlinkCache = symlinkCache;
        public string[] ContentMapperExtensionsValue = [];

        public string GetCurrentDirectory() => CurrentDir;

        public bool UseCaseSensitiveFileNames() => UseCaseSensitiveFileNamesValue;

        public KnownSymlinks? GetSymlinkCache() => SymlinkCache;

        public string GetGlobalTypingsCacheLocation() => "";

        public string CommonSourceDirectory() => CurrentDir;

        public string[] ContentMapperExtensions() => ContentMapperExtensionsValue;

        public SourceOutputAndProjectReference? GetProjectReferenceFromSource(Path path) => null;

        public string[] GetRedirectTargets(Path path) => [];

        public string GetSourceOfProjectReferenceIfOutputIncluded(HasFileName file) => file.FileName();

        public bool FileExists(string path) => true; // Mock implementation

        public string GetNearestAncestorDirectoryWithPackageJson(string dirname) => "";

        public InfoCacheEntry? GetPackageJsonInfo(string pkgJsonPath) => null;

        public ModuleKind GetDefaultResolutionModeForFile(HasFileName file) => ModuleKind.None; // core.ResolutionModeNone

        public ResolvedModule? GetResolvedModuleFromModuleSpecifier(HasFileName file, Node moduleSpecifier) => null;

        public ModuleKind GetModeForUsageLocation(HasFileName file, Node moduleSpecifier) => ModuleKind.None; // core.ResolutionModeNone
    }

    public static TheoryData<string, string, string, bool, int, string[]?> GetEachFileNameOfModuleData => new()
    {
        { "basic file path", "/project/src/main.ts", "/project/lib/utils.ts", false, 1, ["/project/lib/utils.ts"] },
        { "symlink preference false", "/project/src/main.ts", "/project/lib/utils.ts", false, 1, null },
        { "symlink preference true", "/project/src/main.ts", "/project/lib/utils.ts", true, 1, null },
        {
            "ignored path with no alternatives", "/project/src/main.ts", "/project/node_modules/.pnpm/file.ts", false, 1, null
        }, // Should return 1 because there's no better option (all paths are ignored)
    };

    [Theory]
    [MemberData(nameof(GetEachFileNameOfModuleData))]
    public void TestGetEachFileNameOfModule(
        string name,
        string importingFile,
        string importedFile,
        bool preferSymlinks,
        int expectedCount,
        string[]? expectedPaths)
    {
        _ = name;
        var host = new MockModuleSpecifierGenerationHost("/project", true, NewKnownSymlink("/project", true));

        var result = GetEachFileNameOfModule(importingFile, importedFile, host, preferSymlinks);

        Assert.Equal(expectedCount, result.Length);

        if (expectedPaths is not null)
        {
            for (var i = 0; i < expectedPaths.Length; i++)
            {
                if (i >= result.Length)
                {
                    Assert.Fail($"Expected path {i}: {expectedPaths[i]}, but result has only {result.Length} paths");
                    continue;
                }

                Assert.Equal(expectedPaths[i], result[i].FileName);
            }
        }

        for (var i = 0; i < result.Length; i++)
        {
            Assert.NotEqual("", result[i].FileName);
        }
    }

    [Fact]
    public void TestGetEachFileNameOfModuleWithSymlinks()
    {
        var host = new MockModuleSpecifierGenerationHost("/project", true, NewKnownSymlink("/project", true));

        var symlinkPath = TSPath.TSPath.ToPath("/project/symlink", "/project", true).EnsureTrailingDirectorySeparator();
        var realDirectory = new KnownDirectoryLink
        {
            Real = "/real/path/",
            RealPath = TSPath.TSPath.ToPath("/real/path", "/project", true).EnsureTrailingDirectorySeparator(),
        };
        host.SymlinkCache!.SetDirectory("/project/symlink", symlinkPath, realDirectory);

        var result = GetEachFileNameOfModule("/project/src/main.ts", "/real/path/file.ts", host, true);

        // Should find the symlink path
        var found = false;
        foreach (var path in result)
        {
            if (path.FileName == "/project/symlink/file.ts")
            {
                found = true;
                break;
            }
        }

        Assert.True(found, "Expected to find symlink path /project/symlink/file.ts");
    }

    public static TheoryData<string, string, bool> ContainsNodeModulesData => new()
    {
        { "contains node_modules", "/project/node_modules/lodash/index.js", true },
        { "does not contain node_modules", "/project/src/utils.ts", false },
        { "node_modules in middle", "/project/packages/node_modules/pkg/file.js", true },
        { "empty path", "", false },
    };

    [Theory]
    [MemberData(nameof(ContainsNodeModulesData))]
    public void TestContainsNodeModules(string name, string path, bool expected)
    {
        _ = name;
        Assert.Equal(expected, ContainsNodeModules(path));
    }

    public static TheoryData<string, string, bool> ContainsIgnoredPathData => new()
    {
        { "ignored path", "/project/node_modules/.pnpm/file.ts", true },
        { "not ignored path", "/project/src/file.ts", false },
    };

    [Theory]
    [MemberData(nameof(ContainsIgnoredPathData))]
    public void TestContainsIgnoredPath(string name, string path, bool expected)
    {
        _ = name;
        Assert.Equal(expected, ContainsIgnoredPath(path));
    }

    public static TheoryData<string, string, string> TryGetRealFileNameForNonJSDeclarationFileNameData => new()
    {
        { "json declaration file", "/project/foo.d.json.ts", "/project/foo.json" },
        { "multi-dot source extension declaration file", "/project/foo.module.d.css.ts", "/project/foo.module.css" },
        { "plain dts file ignored", "/project/foo.d.ts", "" },
    };

    [Theory]
    [MemberData(nameof(TryGetRealFileNameForNonJSDeclarationFileNameData))]
    public void TestTryGetRealFileNameForNonJSDeclarationFileName(string name, string fileName, string expected)
    {
        _ = name;
        Assert.Equal(expected, TryGetRealFileNameForNonJSDeclarationFileName(fileName));
    }

    public static TheoryData<string, string, string> TryGetModuleNameFromExportsOrImportsData => new()
    {
        { "match", "/pkg/src/things/thing1/index.ts", "./src/things/thing1" },
        { "mismatch with matching leading and trailing strings", "/pkg/src/things/index.ts", "" },
    };

    [Theory]
    [MemberData(nameof(TryGetModuleNameFromExportsOrImportsData))]
    public void TestTryGetModuleNameFromExportsOrImports(string name, string targetFilePath, string expected)
    {
        _ = name;
        var result = TryGetModuleNameFromExportsOrImports(
            new CompilerOptions(),
            new MockModuleSpecifierGenerationHost("", false, null),
            targetFilePath,
            "/pkg",
            "./src/things/*",
            new ExportsOrImports
            {
                Type = JSONValueType.String,
                Value = "./src/things/*/index.js",
            },
            [],
            MatchingMode.Pattern,
            false,
            false);
        Assert.Equal(expected, result);
    }
}
