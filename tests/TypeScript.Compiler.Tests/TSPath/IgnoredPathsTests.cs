// ported from: internal/tspath/ignoredpaths_test.go
using Xunit;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Tests;

public class TspathIgnoredPathsTests
{
    // Go 的表驱动数据表用匿名 struct；C# 用一个 record 保留同样的字段与子测试名（ToString 即 name）。
    public sealed record Case(string Name, string Path, bool Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly Case[] tests =
    [
        new("node_modules dot path", "/project/node_modules/.pnpm/file.ts", true),
        new("git directory", "/project/.git/hooks/pre-commit", true),
        new("emacs lock file", "/project/src/file.ts.#", true),
        new("regular file path", "/project/src/file.ts", false),
        new("node_modules without dot", "/project/node_modules/lodash/index.js", false),
        new("empty path", "", false),
        new("path with multiple ignored patterns", "/project/node_modules/.pnpm/.git/.#file.ts", true),
        // Should be case sensitive
        new("case sensitive test", "/project/NODE_MODULES/.PNPM/file.ts", false),
        new("path with ignored pattern in middle", "/project/src/node_modules/.pnpm/dist/file.js", true),
        new("path with ignored pattern at end", "/project/src/file.ts.#", true),
    ];

    private static readonly Case[] edgeCases =
    [
        // Pattern is "/node_modules/." not "/node_modules."
        new("pattern at start", "/node_modules./file.ts", false),
        new("pattern at end", "/project/file.ts.#", true),
        new("multiple occurrences", "/project/.git/node_modules./.git/file.ts", true),
        new("no slashes", "node_modules.file.ts", false),
        new("single slash", "/file.ts", false),
    ];

    public static IEnumerable<object[]> ContainsIgnoredPathData() => AsRows(tests);

    public static IEnumerable<object[]> IgnoredPathsEdgeCaseData() => AsRows(edgeCases);

    private static IEnumerable<object[]> AsRows(Case[] cases)
    {
        foreach (var testCase in cases)
        {
            yield return new object[] { testCase };
        }
    }

    [Theory]
    [MemberData(nameof(ContainsIgnoredPathData))]
    public void TestContainsIgnoredPath(Case tt) => Assert.Equal(tt.Expected, ContainsIgnoredPath(tt.Path));

    [Fact]
    public void TestIgnoredPathsPatterns()
    {
        // Test that all expected patterns are present
        var expectedPatterns = new[] { "/node_modules/.", "/.git", ".#" };

        foreach (var pattern in expectedPatterns)
        {
            var testPath = "/test" + pattern + "/file.ts";
            Assert.True(ContainsIgnoredPath(testPath));
        }
    }

    [Theory]
    [MemberData(nameof(IgnoredPathsEdgeCaseData))]
    public void TestIgnoredPathsEdgeCases(Case tt) => Assert.Equal(tt.Expected, ContainsIgnoredPath(tt.Path));
}
