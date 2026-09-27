// ported from: internal/vfs/vfsmatch/vfsmatch_test.go（SpecMatcher / IsImplicitGlob / getBasePaths 部分）
//
// Go 的 `NewSpecMatcher(specs, basePath, usage, useCaseSensitiveFileNames)` 在 C# 是
// `TypeScript.Vfs.VfsMatch.VfsMatch.NewSpecMatcher`（返回 `SpecMatcher?`，nil → null）；
// `getBasePaths` / `isImplicitGlob` 未导出 → C# 同名 PascalCase（`GetBasePaths` / `IsImplicitGlob`）。
using TypeScript.Vfs.VfsMatch;
using Xunit;
using static TypeScript.Vfs.VfsMatch.VfsMatch;

namespace TypeScript.Tests;

public class VfsMatchSpecMatcherTests
{
    public sealed record ImplicitGlobCase(string Name, string Input, bool Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly ImplicitGlobCase[] _implicitGlobCases =
    [
        new("simple", "foo", true),
        new("folder", "src", true),
        new("with extension", "foo.ts", false),
        new("trailing dot", "foo.", false),
        new("star", "*", false),
        new("question", "?", false),
        new("star suffix", "foo*", false),
        new("question suffix", "foo?", false),
        new("dot name", "foo.bar", false),
        new("empty", "", true),
    ];

    public static IEnumerable<object[]> IsImplicitGlobData() => _implicitGlobCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(IsImplicitGlobData))]
    public void TestIsImplicitGlob(ImplicitGlobCase tt) => Assert.Equal(tt.Expected, IsImplicitGlob(tt.Input));

    // --- TestSpecMatcher ---

    public sealed record SpecMatcherCase(
        string Name,
        string[] Specs,
        string BasePath,
        Usage Usage,
        bool UseCaseSensitiveFileNames,
        string[] MatchingPaths,
        string[] NonMatchingPaths)
    {
        public override string ToString() => Name;
    }

    private static readonly SpecMatcherCase[] _specMatcherCases =
    [
        new("simple wildcard", ["*.ts"], "/project", Usage.Files, true,
            ["/project/a.ts", "/project/b.ts", "/project/foo.ts"], ["/project/a.js", "/project/sub/a.ts"]),
        new("recursive wildcard", ["**/*.ts"], "/project", Usage.Files, true,
            ["/project/a.ts", "/project/sub/a.ts", "/project/sub/deep/a.ts"], ["/project/a.js"]),
        new("exclude pattern", ["node_modules"], "/project", Usage.Exclude, true,
            ["/project/node_modules/foo"], ["/project/node_modules", "/project/src"]),
        new("case insensitive", ["*.ts"], "/project", Usage.Files, false,
            ["/project/A.TS", "/project/B.Ts"], ["/project/a.js"]),
        new("multiple specs", ["*.ts", "*.tsx"], "/project", Usage.Files, true,
            ["/project/a.ts", "/project/b.tsx"], ["/project/a.js"]),
    ];

    public static IEnumerable<object[]> SpecMatcherData() => _specMatcherCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SpecMatcherData))]
    public void TestSpecMatcher(SpecMatcherCase tc)
    {
        var matcher = NewSpecMatcher(tc.Specs, tc.BasePath, tc.Usage, tc.UseCaseSensitiveFileNames);
        Assert.NotNull(matcher);
        foreach (var path in tc.MatchingPaths)
        {
            Assert.True(matcher!.MatchString(path), "should match: " + path);
        }

        foreach (var path in tc.NonMatchingPaths)
        {
            Assert.False(matcher!.MatchString(path), "should not match: " + path);
        }
    }

    // --- TestSpecMatcher_MatchString ---

    public sealed record SpecMatcherPathsCase(
        string Name,
        string[] Specs,
        string BasePath,
        Usage Usage,
        bool UseCaseSensitiveFileNames,
        string[] Paths,
        bool[] Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly SpecMatcherPathsCase[] _specMatcherMatchStringCases =
    [
        new("simple wildcard files", ["*.ts"], "/project", Usage.Files, true,
            ["/project/a.ts", "/project/sub/a.ts", "/project/a.js"], [true, false, false]),
        new("recursive wildcard files", ["**/*.ts"], "/project", Usage.Files, true,
            ["/project/a.ts", "/project/sub/a.ts", "/project/a.js"], [true, true, false]),
        new("exclude pattern matches prefix", ["node_modules"], "/project", Usage.Exclude, true,
            ["/project/node_modules", "/project/node_modules/foo", "/project/src"], [false, true, false]),
    ];

    public static IEnumerable<object[]> SpecMatcherMatchStringData() => _specMatcherMatchStringCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SpecMatcherMatchStringData))]
    public void TestSpecMatcher_MatchString(SpecMatcherPathsCase tc)
    {
        Assert.Equal(tc.Paths.Length, tc.Expected.Length);
        var m = NewSpecMatcher(tc.Specs, tc.BasePath, tc.Usage, tc.UseCaseSensitiveFileNames);
        Assert.NotNull(m);
        for (var i = 0; i < tc.Paths.Length; i++)
        {
            Assert.Equal(tc.Expected[i], m!.MatchString(tc.Paths[i]));
        }
    }

    // --- TestSingleSpecMatcher_MatchString ---

    public sealed record SingleSpecPathsCase(
        string Name,
        string Spec,
        string BasePath,
        Usage Usage,
        bool UseCaseSensitiveFileNames,
        string[] Paths,
        bool[] Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly SingleSpecPathsCase[] _singleSpecMatcherMatchStringCases =
    [
        new("single spec wildcard", "*.ts", "/project", Usage.Files, true,
            ["/project/a.ts", "/project/sub/a.ts", "/project/a.js"], [true, false, false]),
        new("single spec trailing starstar exclude allowed", "**", "/project", Usage.Exclude, true,
            ["/project/a.ts", "/project/sub/a.ts"], [true, true]),
    ];

    public static IEnumerable<object[]> SingleSpecMatcherMatchStringData() => _singleSpecMatcherMatchStringCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SingleSpecMatcherMatchStringData))]
    public void TestSingleSpecMatcher_MatchString(SingleSpecPathsCase tc)
    {
        Assert.Equal(tc.Paths.Length, tc.Expected.Length);
        var m = NewSpecMatcher([tc.Spec], tc.BasePath, tc.Usage, tc.UseCaseSensitiveFileNames);
        Assert.NotNull(m);
        for (var i = 0; i < tc.Paths.Length; i++)
        {
            Assert.Equal(tc.Expected[i], m!.MatchString(tc.Paths[i]));
        }
    }

    // --- TestSpecMatchers_MatchIndex ---

    public sealed record SpecMatcherIndexCase(
        string Name,
        string[] Specs,
        string BasePath,
        Usage Usage,
        bool UseCaseSensitiveFileNames,
        string[] Paths,
        int[] Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly SpecMatcherIndexCase[] _specMatchersMatchIndexCases =
    [
        new("index lookup prefers first match", ["*.ts", "*.tsx"], "/project", Usage.Files, true,
            ["/project/a.ts", "/project/a.tsx", "/project/a.js"], [0, 1, -1]),
        new("exclude index lookup", ["node_modules", "bower_components"], "/project", Usage.Exclude, true,
            ["/project/node_modules", "/project/node_modules/foo", "/project/bower_components", "/project/bower_components/bar", "/project/src"],
            [-1, 0, -1, 1, -1]),
    ];

    public static IEnumerable<object[]> SpecMatchersMatchIndexData() => _specMatchersMatchIndexCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SpecMatchersMatchIndexData))]
    public void TestSpecMatchers_MatchIndex(SpecMatcherIndexCase tc)
    {
        Assert.Equal(tc.Paths.Length, tc.Expected.Length);
        var m = NewSpecMatcher(tc.Specs, tc.BasePath, tc.Usage, tc.UseCaseSensitiveFileNames);
        Assert.NotNull(m);
        for (var i = 0; i < tc.Paths.Length; i++)
        {
            Assert.Equal(tc.Expected[i], m!.MatchIndex(tc.Paths[i]));
        }
    }

    // --- TestSingleSpecMatcher ---

    public sealed record SingleSpecMatcherCase(
        string Name,
        string Spec,
        string BasePath,
        Usage Usage,
        bool UseCaseSensitiveFileNames,
        bool ExpectNil,
        string[] MatchingPaths,
        string[] NonMatchingPaths)
    {
        public override string ToString() => Name;
    }

    private static readonly SingleSpecMatcherCase[] _singleSpecMatcherCases =
    [
        new("simple spec", "*.ts", "/project", Usage.Files, true, false, ["/project/a.ts"], ["/project/a.js"]),
        new("trailing ** non-exclude returns nil", "**", "/project", Usage.Files, true, true, [], []),
        new("trailing ** exclude works", "**", "/project", Usage.Exclude, true, false, ["/project/anything", "/project/deep/path"], []),
    ];

    public static IEnumerable<object[]> SingleSpecMatcherData() => _singleSpecMatcherCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SingleSpecMatcherData))]
    public void TestSingleSpecMatcher(SingleSpecMatcherCase tc)
    {
        var matcher = NewSpecMatcher([tc.Spec], tc.BasePath, tc.Usage, tc.UseCaseSensitiveFileNames);
        if (tc.ExpectNil)
        {
            Assert.Null(matcher);
            return;
        }

        Assert.NotNull(matcher);
        foreach (var path in tc.MatchingPaths)
        {
            Assert.True(matcher!.MatchString(path), "should match: " + path);
        }

        foreach (var path in tc.NonMatchingPaths)
        {
            Assert.False(matcher!.MatchString(path), "should not match: " + path);
        }
    }

    // --- TestSpecMatchers ---

    public sealed record SpecMatchersCase(
        string Name,
        string[] Specs,
        string BasePath,
        Usage Usage,
        bool UseCaseSensitiveFileNames,
        bool ExpectNil,
        Dictionary<string, int> PathToIndex)
    {
        public override string ToString() => Name;
    }

    private static readonly SpecMatchersCase[] _specMatchersCases =
    [
        new("multiple specs return correct index", ["*.ts", "*.tsx", "*.js"], "/project", Usage.Files, true, false,
            new Dictionary<string, int>
            {
                ["/project/a.ts"] = 0,
                ["/project/b.tsx"] = 1,
                ["/project/c.js"] = 2,
                ["/project/d.css"] = -1,
            }),
        new("empty specs returns nil", [], "/project", Usage.Files, true, true, []),
    ];

    public static IEnumerable<object[]> SpecMatchersData() => _specMatchersCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SpecMatchersData))]
    public void TestSpecMatchers(SpecMatchersCase tc)
    {
        var matchers = NewSpecMatcher(tc.Specs, tc.BasePath, tc.Usage, tc.UseCaseSensitiveFileNames);
        if (tc.ExpectNil)
        {
            Assert.Null(matchers);
            return;
        }

        Assert.NotNull(matchers);
        foreach (var (path, expectedIndex) in tc.PathToIndex)
        {
            Assert.Equal(expectedIndex, matchers!.MatchIndex(path));
        }
    }

    // --- TestGetBasePathsCaseSensitivity ---

    // getBasePaths 的 base path 去重必须用调用方给的大小写敏感度：大小写敏感时 "/Other" 与 "/other" 是两个目录。
    [Fact]
    public void TestGetBasePathsCaseSensitivity_CaseSensitiveDoesNotDedupDifferentlyCasedPaths()
    {
        var basePaths = GetBasePaths("/root", ["../Other/**/*.ts", "../other/**/*.ts"], true);
        Assert.Contains("/Other", basePaths);
        Assert.Contains("/other", basePaths);
    }

    [Fact]
    public void TestGetBasePathsCaseSensitivity_CaseInsensitiveDedupsDifferentlyCasedPaths()
    {
        var basePaths = GetBasePaths("/root", ["../Other/**/*.ts", "../other/**/*.ts"], false);
        var count = basePaths.Count(bp => bp is "/Other" or "/other");
        Assert.True(count <= 1, "expected at most one of /Other or /other in base paths");
    }
}
