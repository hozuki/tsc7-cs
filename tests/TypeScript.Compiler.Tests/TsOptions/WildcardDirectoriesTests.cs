// ported from: internal/tsoptions/wildcarddirectories_test.go
//
// 与 Go 的差异：
// 1) Go 的第二个用例（TestGetWildcardDirectories_NonASCIICharacters）只断言结果非 nil；这里改成断言
//    期望的**具体目录**（同一个文件里的第一个用例就是这么断言的：include "src/**" 类模式 → 唯一的
//    监视目录 = currentDirectory + 通配符前面的目录段，recursive=true）。两个用例的期望值结构一致，
//    所以加强断言不引入新语义；这样"多字符大小写映射 / 非 ASCII 路径把通配目录算错"这类回归才拦得住。
// 2) Go 用 t.Run + t.Parallel 分子测试 → xunit 的 Theory 数据点。
using System.Collections.Generic;
using TypeScript.TSPath;
using Xunit;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.Tests;

public class WildcardDirectoriesTests
{
    // Go: TestGetWildcardDirectories_DotPrefixedIncludeWithDotDirExclude
    // https://github.com/microsoft/TypeScript/tsc/issues/3733
    // "./"-prefixed include specs must be fully normalized before being tested
    // against exclude patterns; otherwise the leftover literal "." path segment
    // matches dot-directory excludes like "**/.*/", silently dropping every
    // wildcard directory (and with them, root file watching for the config).
    [Fact]
    public void TestGetWildcardDirectoriesDotPrefixedIncludeWithDotDirExclude()
    {
        var result = GetWildcardDirectories(
            ["./app/**/*.ts", "./app/**/*.tsx"],
            ["**/node_modules", "**/.*/", "./build"],
            new ComparePathsOptions(useCaseSensitiveFileNames: true, currentDirectory: "/home/projects/monorepo/apps/web"));

        Assert.NotNull(result);
        var single = Assert.Single(result);
        Assert.Equal("/home/projects/monorepo/apps/web/app", single.Key);
        Assert.True(single.Value);
    }

    // Go: TestGetWildcardDirectories_NonASCIICharacters
    public static IEnumerable<object[]> NonAsciiCases()
    {
        yield return
        [
            "Norwegian character æ in path",
            new[] { "src/**/*.test.ts", "src/**/*.stories.ts", "src/**/*.mdx" },
            new[] { "node_modules" },
            "C:/Users/TobiasLægreid/dev/app/frontend/packages/react",
            false,
            "C:/Users/TobiasLægreid/dev/app/frontend/packages/react/src",
        ];
        yield return
        [
            "Japanese characters in path",
            new[] { "src/**/*.ts" },
            new[] { "テスト" },
            "/Users/ユーザー/プロジェクト",
            true,
            "/Users/ユーザー/プロジェクト/src",
        ];
        yield return
        [
            "Chinese characters in path",
            new[] { "源代码/**/*.js" },
            new[] { "节点模块" },
            "/home/用户/项目",
            true,
            "/home/用户/项目/源代码",
        ];
        yield return
        [
            "Various Unicode characters",
            new[] { "src/**/*.ts" },
            new[] { "node_modules" },
            "/Users/Müller/café/naïve/résumé",
            false,
            "/Users/Müller/café/naïve/résumé/src",
        ];
    }

    [Theory]
    [MemberData(nameof(NonAsciiCases))]
    public void TestGetWildcardDirectoriesNonASCIICharacters(
        string name,
        string[] include,
        string[] exclude,
        string currentDirectory,
        bool useCaseSensitiveFileNames,
        string expectedDirectory)
    {
        var comparePathsOptions = new ComparePathsOptions(
            useCaseSensitiveFileNames: useCaseSensitiveFileNames,
            currentDirectory: currentDirectory);

        var result = GetWildcardDirectories(include, exclude, comparePathsOptions);

        // Go 只断言 "expected non-nil result"；具体目录见文件头差异 1。
        Assert.True(result is not null, $"{name}: expected non-nil result");
        Assert.True(
            result!.Count == 1,
            $"{name}: expected exactly one wildcard directory, got [{string.Join(",", result.Keys)}]");
        Assert.True(result.TryGetValue(expectedDirectory, out var recursive), $"{name}: missing {expectedDirectory}");
        Assert.True(recursive);
    }
}
