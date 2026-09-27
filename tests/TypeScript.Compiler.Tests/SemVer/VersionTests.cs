// ported from: internal/semver/version_test.go
//
// 移植映射（docs/architecture.md §5）：
//   * Go 的 `[]struct{…}` 表驱动 + t.Run(name, …) + t.Parallel() → xunit 的 [Theory] + [MemberData]
//     （每条用例各自并行，与 Go 的 t.Parallel() 相当）；用例名由 ITestOutputHelper 打出。
//   * `TestVersionCompare` 的 want 用 Go 的包级常量 `comparisonLessThan/EqualTo/GreaterThan`
//     （C# 侧是 `SemVer` 静态类的 internal const `_comparisonLessThan…`，靠 InternalsVisibleTo 可见）。
//   * assert.NilError / assert.Equal / assert.DeepEqual → Assert.True / Assert.Equal；
//     `assertVersion` 直接比较结构体字段（`Version` 的字段是 internal，与 Go 同 package 的情况一致）。
//   * Go 的 `Version{major: …, prerelease: []string{…}}` 复合字面量 → internal 构造器的具名参数
//     （见 src/TypeScript.Compiler/SemVer/Version.cs 文件头第 3 条）。
using TypeScript.SemVer;
// TypeScript.SemVer.Version 与 System.Version 在 implicit usings(using System) 下会撞名，显式取别名。
using Version = TypeScript.SemVer.Version;
using Xunit;
using Xunit.Abstractions;
using static TypeScript.SemVer.SemVer;

namespace TypeScript.Tests;

public class VersionTests(ITestOutputHelper output)
{
    public sealed record TryParseSemverCase(string Input, Version Expected);

    public sealed record VersionStringCase(Version In, string Expected);

    public static TheoryData<TryParseSemverCase> TryParseSemverCases => new()
    {
        new("1.2.3-pre.4+build.5", new Version(major: 1, minor: 2, patch: 3, prerelease: ["pre", "4"], build: ["build", "5"])),
        new("1.2.3-pre.4", new Version(major: 1, minor: 2, patch: 3, prerelease: ["pre", "4"])),
        new("1.2.3+build.4", new Version(major: 1, minor: 2, patch: 3, build: ["build", "4"])),
        new("1.2.3", new Version(major: 1, minor: 2, patch: 3)),
    };

    public static TheoryData<VersionStringCase> VersionStringCases => new()
    {
        new(new Version(major: 1, minor: 2, patch: 3, prerelease: ["pre", "4"], build: ["build", "5"]), "1.2.3-pre.4+build.5"),
        new(new Version(major: 1, minor: 2, patch: 3, prerelease: ["pre", "4"], build: ["build"]), "1.2.3-pre.4+build"),
        new(new Version(major: 1, minor: 2, patch: 3, build: ["build"]), "1.2.3+build"),
        new(new Version(major: 1, minor: 2, patch: 3, prerelease: ["pre", "4"]), "1.2.3-pre.4"),
        new(new Version(major: 1, minor: 2, patch: 3, build: ["build", "4"]), "1.2.3+build.4"),
        new(new Version(major: 1, minor: 2, patch: 3), "1.2.3"),
    };

    public static TheoryData<string, string, int> VersionCompareCases => new()
    {
        // https://semver.org/#spec-item-11
        // > Precedence is determined by the first difference when comparing each of these
        // > identifiers from left to right as follows: Major, minor, and patch versions are
        // > always compared numerically.
        { "1.0.0", "2.0.0", _comparisonLessThan },
        { "1.0.0", "1.1.0", _comparisonLessThan },
        { "1.0.0", "1.0.1", _comparisonLessThan },
        { "2.0.0", "1.0.0", _comparisonGreaterThan },
        { "1.1.0", "1.0.0", _comparisonGreaterThan },
        { "1.0.1", "1.0.0", _comparisonGreaterThan },
        { "1.0.0", "1.0.0", _comparisonEqualTo },

        // https://semver.org/#spec-item-11
        // > When major, minor, and patch are equal, a pre-release version has lower
        // > precedence than a normal version.
        { "1.0.0", "1.0.0-pre", _comparisonGreaterThan },
        { "1.0.1-pre", "1.0.0", _comparisonGreaterThan },
        { "1.0.0-pre", "1.0.0", _comparisonLessThan },

        // https://semver.org/#spec-item-11
        // > identifiers consisting of only digits are compared numerically
        { "1.0.0-0", "1.0.0-1", _comparisonLessThan },
        { "1.0.0-1", "1.0.0-0", _comparisonGreaterThan },
        { "1.0.0-2", "1.0.0-10", _comparisonLessThan },
        { "1.0.0-10", "1.0.0-2", _comparisonGreaterThan },
        { "1.0.0-0", "1.0.0-0", _comparisonEqualTo },

        // https://semver.org/#spec-item-11
        // > identifiers with letters or hyphens are compared lexically in ASCII sort order.
        { "1.0.0-a", "1.0.0-b", _comparisonLessThan },
        { "1.0.0-a-2", "1.0.0-a-10", _comparisonGreaterThan },
        { "1.0.0-b", "1.0.0-a", _comparisonGreaterThan },
        { "1.0.0-a", "1.0.0-a", _comparisonEqualTo },
        { "1.0.0-A", "1.0.0-a", _comparisonLessThan },

        // https://semver.org/#spec-item-11
        // > Numeric identifiers always have lower precedence than non-numeric identifiers.
        { "1.0.0-0", "1.0.0-alpha", _comparisonLessThan },
        { "1.0.0-alpha", "1.0.0-0", _comparisonGreaterThan },
        { "1.0.0-0", "1.0.0-0", _comparisonEqualTo },
        { "1.0.0-alpha", "1.0.0-alpha", _comparisonEqualTo },

        // https://semver.org/#spec-item-11
        // > A larger set of pre-release fields has a higher precedence than a smaller set, if all
        // > of the preceding identifiers are equal.
        { "1.0.0-alpha", "1.0.0-alpha.0", _comparisonLessThan },
        { "1.0.0-alpha.0", "1.0.0-alpha", _comparisonGreaterThan },

        // https://semver.org/#spec-item-11
        // > Precedence for two pre-release versions with the same major, minor, and patch version
        // > MUST be determined by comparing each dot separated identifier from left to right until
        // > a difference is found [...]
        { "1.0.0-a.0.b.1", "1.0.0-a.0.b.2", _comparisonLessThan },
        { "1.0.0-a.0.b.1", "1.0.0-b.0.a.1", _comparisonLessThan },
        { "1.0.0-a.0.b.2", "1.0.0-a.0.b.1", _comparisonGreaterThan },
        { "1.0.0-b.0.a.1", "1.0.0-a.0.b.1", _comparisonGreaterThan },

        // https://semver.org/#spec-item-11
        // > Build metadata does not figure into precedence
        { "1.0.0+build", "1.0.0", _comparisonEqualTo },
        { "1.0.0+build.stuff", "1.0.0", _comparisonEqualTo },
        { "1.0.0", "1.0.0+build", _comparisonEqualTo },
        { "1.0.0+build", "1.0.0+stuff", _comparisonEqualTo },

        // https://semver.org/#spec-item-11
        // Edge cases for numeric and lexical comparison of prerelease identifiers.
        { "1.0.0-alpha.99999", "1.0.0-alpha.100000", _comparisonLessThan },
        { "1.0.0-alpha.beta", "1.0.0-alpha.alpha", _comparisonGreaterThan },
    };

    [Theory]
    [MemberData(nameof(TryParseSemverCases))]
    public void TestTryParseSemver(TryParseSemverCase test)
    {
        output.WriteLine(test.Input);

        Assert.True(TryParseVersion(test.Input, out var v));
        AssertVersion(v, test.Expected);
    }

    [Theory]
    [MemberData(nameof(VersionStringCases))]
    public void TestVersionString(VersionStringCase test)
    {
        output.WriteLine(test.In.ToString()); // Go: t.Run(test.out, …)

        Assert.Equal(test.Expected, test.In.ToString());
    }

    [Theory]
    [MemberData(nameof(VersionCompareCases))]
    public void TestVersionCompare(string v1Text, string v2Text, int want)
    {
        output.WriteLine($"{v1Text} <=> {v2Text}");

        Assert.True(TryParseVersion(v1Text, out var v1));
        Assert.True(TryParseVersion(v2Text, out var v2));
        Assert.Equal(want, v1.Compare(v2));
    }

    // Go: assertVersion(t, a, b) —— 逐字段比较（Go 的 assert.DeepEqual 对 slice 是逐元素比较）。
    private static void AssertVersion(Version a, Version b)
    {
        Assert.Equal(b._major, a._major);
        Assert.Equal(b._minor, a._minor);
        Assert.Equal(b._patch, a._patch);
        AssertSequenceEqual(b._prerelease, a._prerelease);
        AssertSequenceEqual(b._build, a._build);
    }

    private static void AssertSequenceEqual(string[]? expected, string[]? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }
}
