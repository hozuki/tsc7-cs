// ported from: internal/semver/version_range_test.go
//
// 移植映射（docs/architecture.md §5）：
//   * Go 的 t.Run(name, …) + t.Parallel() → xunit 的 [Theory]/[MemberData]：每个 Go 顶层 Test* 一个测试方法，
//     Go 里每个 t.Run 用例 → [MemberData] 里的一行（用例名通过 ITestOutputHelper 打印，与 Go 的 fmt.Sprintf 格式一致）。
//   * `TestWildcardsHaveSameString` 的"每组内两两比较"用 MemberData 里现场生成的全部 (s1, s2) 对复刻
//     （数据量与 Go 的三重循环完全相同，只是不写死 4×(n²) 行字面量）。
//   * `TestVersionRanges` 的 `assertRangesGoodBad`（每个 range 一组 good/bad）与
//     `TestComparators/Conjunctions/Disjunctions/Hyphens/Tildes/CaretsOfVersionRanges` 的
//     `[]testForRangeOnVersion` 表 → 统一的 (rangeText, versionText, expected) 三元组。
//   * Go 的 `lotsaOnes := strings.Repeat("1", 320)` → `_lotsaOnes = new string('1', 320)`（下方字段），
//     数据行里的 `"1.2.3-1" + lotsaOnes` 原样保留成 C# 的字符串拼接。
//   * assert.Assert(t, cond) → Assert.True(cond)；assert.DeepEqual(t, a, b) → Assert.Equal。
//   * Go 的 `versionRange.Test(&v)` → `versionRange.Test(v)`（`in` 参数调用点可省略修饰符，见契约）。
using TypeScript.SemVer;
// TypeScript.SemVer.Version 与 System.Version 在 implicit usings(using System) 下会撞名，显式取别名。
using Version = TypeScript.SemVer.Version;
using Xunit;
using Xunit.Abstractions;
using static TypeScript.SemVer.SemVer;

namespace TypeScript.Tests;

public class VersionRangeTests(ITestOutputHelper output)
{
    // Go: lotsaOnes := strings.Repeat("1", 320)
    private static readonly string _lotsaOnes = new('1', 320);

    // Go 的 assertAllVersionRangesHaveIdenticalStrings：对每组字符串两两比较 String()。
    public static TheoryData<string, string> WildcardStringCases
    {
        get
        {
            string[][] groups =
            [
                // majorWildcardStrings
                ["", "*", "*.*", "*.*.*", "x", "x.x", "x.x.x", "X", "X.X", "X.X.X"],
                // minorWildcardStrings
                ["1", "1.*", "1.*.*", "1.x", "1.x.x", "1.X", "1.X.X"],
                // patchWildcardStrings
                ["1.2", "1.2.*", "1.2.x", "1.2.X"],
                // mixedCaseWildcardStrings
                ["x", "X", "*", "x.X.x", "X.x.*"],
            ];

            var data = new TheoryData<string, string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                foreach (var s1 in group)
                {
                    foreach (var s2 in group)
                    {
                        // 同一个 (s1, s2) 可能同时出现在多个组里（Go 会重复断言一次）；xunit 对重复 ID 的用例
                        // 会跳过并打日志，这里按 Go 的组序去重，断言集合与 Go 完全一致。
                        if (seen.Add(s1 + "\u0000" + s2))
                        {
                            data.Add(s1, s2);
                        }
                    }
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string, bool> VersionRangesCases => new()
    {
        { "1", "1.0.0", true },
        { "1", "1.9.9", true },
        { "1", "1.0.0-pre", true },
        { "1", "1.0.0+build", true },
        { "1", "0.0.0", false },
        { "1", "2.0.0", false },
        { "1", "0.0.0-pre", false },
        { "1", "0.0.0+build", false },
        { "1.2", "1.2.0", true },
        { "1.2", "1.2.9", true },
        { "1.2", "1.2.0-pre", true },
        { "1.2", "1.2.0+build", true },
        { "1.2", "1.1.0", false },
        { "1.2", "1.3.0", false },
        { "1.2", "1.1.0-pre", false },
        { "1.2", "1.1.0+build", false },
        { "1.2.3", "1.2.3", true },
        { "1.2.3", "1.2.3+build", true },
        { "1.2.3", "1.2.2", false },
        { "1.2.3", "1.2.4", false },
        { "1.2.3", "1.2.2-pre", false },
        { "1.2.3", "1.2.2+build", false },
        { "1.2.3", "1.2.3-pre", false },
        { "1.2.3-pre", "1.2.3-pre", true },
        { "1.2.3-pre", "1.2.3-pre+build.stuff", true },
        { "1.2.3-pre", "1.2.3", false },
        { "1.2.3-pre", "1.2.3-pre.0", false },
        { "1.2.3-pre", "1.2.3-pre.9", false },
        { "1.2.3-pre", "1.2.3-pre.0+build", false },
        { "1.2.3-pre", "1.2.3-pre.9+build", false },
        { "1.2.3-pre", "1.2.3+build", false },
        { "1.2.3-pre", "1.2.4", false },
        { "<3.8.0", "3.6", true },
        { "<3.8.0", "3.7", true },
        { "<3.8.0", "3.8", false },
        { "<3.8.0", "3.9", false },
        { "<3.8.0", "4.0", false },
        { "<=3.8.0", "3.6", true },
        { "<=3.8.0", "3.7", true },
        { "<=3.8.0", "3.8", true },
        { "<=3.8.0", "3.9", false },
        { "<=3.8.0", "4.0", false },
        { ">3.8.0", "3.9", true },
        { ">3.8.0", "4.0", true },
        { ">3.8.0", "3.6", false },
        { ">3.8.0", "3.7", false },
        { ">3.8.0", "3.8", false },
        { ">=3.8.0", "3.8", true },
        { ">=3.8.0", "3.9", true },
        { ">=3.8.0", "4.0", true },
        { ">=3.8.0", "3.6", false },
        { ">=3.8.0", "3.7", false },
        { "<3.8.0-0", "3.6", true },
        { "<3.8.0-0", "3.7", true },
        { "<3.8.0-0", "3.8", false },
        { "<3.8.0-0", "3.9", false },
        { "<3.8.0-0", "4.0", false },
        { "<=3.8.0-0", "3.6", true },
        { "<=3.8.0-0", "3.7", true },
        { "<=3.8.0-0", "3.8", false },
        { "<=3.8.0-0", "3.9", false },
        { "<=3.8.0-0", "4.0", false },
        { ">=1.2.3-1" + _lotsaOnes, "1.2.3-1" + _lotsaOnes, true },
        { ">=1.2.3-1" + _lotsaOnes, "1.2.3-11" + _lotsaOnes + ".1", true },
        { ">=1.2.3-1" + _lotsaOnes, "1.2.3-1" + _lotsaOnes + ".1+build", true },
        { ">=1.2.3-1" + _lotsaOnes, "1.2.3-" + _lotsaOnes + ".1+build", false },
    };

    public static TheoryData<string, string, bool> ComparatorsCases => new()
    {
        // empty (matches everything)
        { "", "2.0.0", true },
        { "", "2.0.0-0", true },
        { "", "1.1.0", true },
        { "", "1.1.0-0", true },
        { "", "1.0.1", true },
        { "", "1.0.1-0", true },
        { "", "1.0.0", true },
        { "", "1.0.0-0", true },
        { "", "0.0.0", true },
        { "", "0.0.0-0", true },

        // wildcard major (matches everything)
        { "*", "2.0.0", true },
        { "*", "2.0.0-0", true },
        { "*", "1.1.0", true },
        { "*", "1.1.0-0", true },
        { "*", "1.0.1", true },
        { "*", "1.0.1-0", true },
        { "*", "1.0.0", true },
        { "*", "1.0.0-0", true },
        { "*", "0.0.0", true },
        { "*", "0.0.0-0", true },

        // wildcard minor
        { "1", "2.0.0", false },
        { "1", "2.0.0-0", false },
        { "1", "1.1.0", true },
        { "1", "1.1.0-0", true },
        { "1", "1.0.1", true },
        { "1", "1.0.1-0", true },
        { "1", "1.0.0", true },
        { "1", "1.0.0-0", true },
        { "1", "0.0.0", false },
        { "1", "0.0.0-0", false },

        // wildcard patch
        { "1.1", "2.0.0", false },
        { "1.1", "2.0.0-0", false },
        { "1.1", "1.1.0", true },
        { "1.1", "1.1.0-0", true },
        { "1.1", "1.0.1", false },
        { "1.1", "1.0.1-0", false },
        { "1.1", "1.0.0", false },
        { "1.1", "1.0.0-0", false },
        { "1.1", "0.0.0", false },
        { "1.1", "0.0.0-0", false },
        { "1.0", "2.0.0", false },
        { "1.0", "2.0.0-0", false },
        { "1.0", "1.1.0", false },
        { "1.0", "1.1.0-0", false },
        { "1.0", "1.0.1", true },
        { "1.0", "1.0.1-0", true },
        { "1.0", "1.0.0", true },
        { "1.0", "1.0.0-0", true },
        { "1.0", "0.0.0", false },
        { "1.0", "0.0.0-0", false },

        // exact
        { "1.1.0", "2.0.0", false },
        { "1.1.0", "2.0.0-0", false },
        { "1.1.0", "1.1.0", true },
        { "1.1.0", "1.1.0-0", false },
        { "1.1.0", "1.0.1", false },
        { "1.1.0", "1.0.1-0", false },
        { "1.1.0", "1.0.0-0", false },
        { "1.1.0", "1.0.0", false },
        { "1.1.0", "0.0.0", false },
        { "1.1.0", "0.0.0-0", false },
        { "1.1.0-0", "2.0.0", false },
        { "1.1.0-0", "2.0.0-0", false },
        { "1.1.0-0", "1.1.0", false },
        { "1.1.0-0", "1.1.0-0", true },
        { "1.1.0-0", "1.0.1", false },
        { "1.1.0-0", "1.0.1-0", false },
        { "1.1.0-0", "1.0.0-0", false },
        { "1.1.0-0", "1.0.0", false },
        { "1.1.0-0", "0.0.0", false },
        { "1.1.0-0", "0.0.0-0", false },
        { "1.0.1", "2.0.0", false },
        { "1.0.1", "2.0.0-0", false },
        { "1.0.1", "1.1.0", false },
        { "1.0.1", "1.1.0-0", false },
        { "1.0.1", "1.0.1", true },
        { "1.0.1", "1.0.1-0", false },
        { "1.0.1", "1.0.0-0", false },
        { "1.0.1", "1.0.0", false },
        { "1.0.1", "0.0.0", false },
        { "1.0.1", "0.0.0-0", false },
        { "1.0.1-0", "2.0.0", false },
        { "1.0.1-0", "2.0.0-0", false },
        { "1.0.1-0", "1.1.0", false },
        { "1.0.1-0", "1.1.0-0", false },
        { "1.0.1-0", "1.0.1", false },
        { "1.0.1-0", "1.0.1-0", true },
        { "1.0.1-0", "1.0.0-0", false },
        { "1.0.1-0", "1.0.0", false },
        { "1.0.1-0", "0.0.0", false },
        { "1.0.1-0", "0.0.0-0", false },
        { "1.0.0", "2.0.0", false },
        { "1.0.0", "2.0.0-0", false },
        { "1.0.0", "1.1.0", false },
        { "1.0.0", "1.1.0-0", false },
        { "1.0.0", "1.0.1", false },
        { "1.0.0", "1.0.1-0", false },
        { "1.0.0", "1.0.0-0", false },
        { "1.0.0", "1.0.0", true },
        { "1.0.0", "0.0.0", false },
        { "1.0.0", "0.0.0-0", false },
        { "1.0.0-0", "2.0.0", false },
        { "1.0.0-0", "2.0.0-0", false },
        { "1.0.0-0", "1.1.0", false },
        { "1.0.0-0", "1.1.0-0", false },
        { "1.0.0-0", "1.0.1", false },
        { "1.0.0-0", "1.0.1-0", false },
        { "1.0.0-0", "1.0.0", false },
        { "1.0.0-0", "1.0.0-0", true },

        // = wildcard major (matches everything)
        { "=*", "2.0.0", true },
        { "=*", "2.0.0-0", true },
        { "=*", "1.1.0", true },
        { "=*", "1.1.0-0", true },
        { "=*", "1.0.1", true },
        { "=*", "1.0.1-0", true },
        { "=*", "1.0.0", true },
        { "=*", "1.0.0-0", true },
        { "=*", "0.0.0", true },
        { "=*", "0.0.0-0", true },

        // = wildcard minor
        { "=1", "2.0.0", false },
        { "=1", "2.0.0-0", false },
        { "=1", "1.1.0", true },
        { "=1", "1.1.0-0", true },
        { "=1", "1.0.1", true },
        { "=1", "1.0.1-0", true },
        { "=1", "1.0.0", true },
        { "=1", "1.0.0-0", true },
        { "=1", "0.0.0", false },
        { "=1", "0.0.0-0", false },

        // = wildcard patch
        { "=1.1", "2.0.0", false },
        { "=1.1", "2.0.0-0", false },
        { "=1.1", "1.1.0", true },
        { "=1.1", "1.1.0-0", true },
        { "=1.1", "1.0.1", false },
        { "=1.1", "1.0.1-0", false },
        { "=1.1", "1.0.0", false },
        { "=1.1", "1.0.0-0", false },
        { "=1.1", "0.0.0", false },
        { "=1.1", "0.0.0-0", false },
        { "=1.0", "2.0.0", false },
        { "=1.0", "2.0.0-0", false },
        { "=1.0", "1.1.0", false },
        { "=1.0", "1.1.0-0", false },
        { "=1.0", "1.0.1", true },
        { "=1.0", "1.0.1-0", true },
        { "=1.0", "1.0.0", true },
        { "=1.0", "1.0.0-0", true },
        { "=1.0", "0.0.0", false },
        { "=1.0", "0.0.0-0", false },

        // = exact
        { "=1.1.0", "2.0.0", false },
        { "=1.1.0", "2.0.0-0", false },
        { "=1.1.0", "1.1.0", true },
        { "=1.1.0", "1.1.0-0", false },
        { "=1.1.0", "1.0.1", false },
        { "=1.1.0", "1.0.1-0", false },
        { "=1.1.0", "1.0.0-0", false },
        { "=1.1.0", "1.0.0", false },
        { "=1.1.0", "0.0.0", false },
        { "=1.1.0", "0.0.0-0", false },
        { "=1.1.0-0", "2.0.0", false },
        { "=1.1.0-0", "2.0.0-0", false },
        { "=1.1.0-0", "1.1.0", false },
        { "=1.1.0-0", "1.1.0-0", true },
        { "=1.1.0-0", "1.0.1", false },
        { "=1.1.0-0", "1.0.1-0", false },
        { "=1.1.0-0", "1.0.0-0", false },
        { "=1.1.0-0", "1.0.0", false },
        { "=1.1.0-0", "0.0.0", false },
        { "=1.1.0-0", "0.0.0-0", false },
        { "=1.0.1", "2.0.0", false },
        { "=1.0.1", "2.0.0-0", false },
        { "=1.0.1", "1.1.0", false },
        { "=1.0.1", "1.1.0-0", false },
        { "=1.0.1", "1.0.1", true },
        { "=1.0.1", "1.0.1-0", false },
        { "=1.0.1", "1.0.0-0", false },
        { "=1.0.1", "1.0.0", false },
        { "=1.0.1", "0.0.0", false },
        { "=1.0.1", "0.0.0-0", false },
        { "=1.0.1-0", "2.0.0", false },
        { "=1.0.1-0", "2.0.0-0", false },
        { "=1.0.1-0", "1.1.0", false },
        { "=1.0.1-0", "1.1.0-0", false },
        { "=1.0.1-0", "1.0.1", false },
        { "=1.0.1-0", "1.0.1-0", true },
        { "=1.0.1-0", "1.0.0-0", false },
        { "=1.0.1-0", "1.0.0", false },
        { "=1.0.1-0", "0.0.0", false },
        { "=1.0.1-0", "0.0.0-0", false },
        { "=1.0.0", "2.0.0", false },
        { "=1.0.0", "2.0.0-0", false },
        { "=1.0.0", "1.1.0", false },
        { "=1.0.0", "1.1.0-0", false },
        { "=1.0.0", "1.0.1", false },
        { "=1.0.0", "1.0.1-0", false },
        { "=1.0.0", "1.0.0-0", false },
        { "=1.0.0", "1.0.0", true },
        { "=1.0.0", "0.0.0", false },
        { "=1.0.0", "0.0.0-0", false },
        { "=1.0.0-0", "2.0.0", false },
        { "=1.0.0-0", "2.0.0-0", false },
        { "=1.0.0-0", "1.1.0", false },
        { "=1.0.0-0", "1.1.0-0", false },
        { "=1.0.0-0", "1.0.1", false },
        { "=1.0.0-0", "1.0.1-0", false },
        { "=1.0.0-0", "1.0.0", false },
        { "=1.0.0-0", "1.0.0-0", true },

        // > wildcard major (matches nothing)
        { ">*", "2.0.0", false },
        { ">*", "2.0.0-0", false },
        { ">*", "1.1.0", false },
        { ">*", "1.1.0-0", false },
        { ">*", "1.0.1", false },
        { ">*", "1.0.1-0", false },
        { ">*", "1.0.0", false },
        { ">*", "1.0.0-0", false },
        { ">*", "0.0.0", false },
        { ">*", "0.0.0-0", false },

        // > wildcard minor
        { ">1", "2.0.0", true },
        { ">1", "2.0.0-0", true },
        { ">1", "1.1.0", false },
        { ">1", "1.1.0-0", false },
        { ">1", "1.0.1", false },
        { ">1", "1.0.1-0", false },
        { ">1", "1.0.0", false },
        { ">1", "1.0.0-0", false },
        { ">1", "0.0.0", false },
        { ">1", "0.0.0-0", false },

        // > wildcard patch
        { ">1.1", "2.0.0", true },
        { ">1.1", "2.0.0-0", true },
        { ">1.1", "1.1.0", false },
        { ">1.1", "1.1.0-0", false },
        { ">1.1", "1.0.1", false },
        { ">1.1", "1.0.1-0", false },
        { ">1.1", "1.0.0", false },
        { ">1.1", "1.0.0-0", false },
        { ">1.1", "0.0.0", false },
        { ">1.1", "0.0.0-0", false },
        { ">1.0", "2.0.0", true },
        { ">1.0", "2.0.0-0", true },
        { ">1.0", "1.1.0", true },
        { ">1.0", "1.1.0-0", true },
        { ">1.0", "1.0.1", false },
        { ">1.0", "1.0.1-0", false },
        { ">1.0", "1.0.0", false },
        { ">1.0", "1.0.0-0", false },
        { ">1.0", "0.0.0", false },
        { ">1.0", "0.0.0-0", false },

        // > exact
        { ">1.1.0", "2.0.0", true },
        { ">1.1.0", "2.0.0-0", true },
        { ">1.1.0", "1.1.0", false },
        { ">1.1.0", "1.1.0-0", false },
        { ">1.1.0", "1.0.1", false },
        { ">1.1.0", "1.0.1-0", false },
        { ">1.1.0", "1.0.0", false },
        { ">1.1.0", "1.0.0-0", false },
        { ">1.1.0", "0.0.0", false },
        { ">1.1.0", "0.0.0-0", false },
        { ">1.1.0-0", "2.0.0", true },
        { ">1.1.0-0", "2.0.0-0", true },
        { ">1.1.0-0", "1.1.0", true },
        { ">1.1.0-0", "1.1.0-0", false },
        { ">1.1.0-0", "1.0.1", false },
        { ">1.1.0-0", "1.0.1-0", false },
        { ">1.1.0-0", "1.0.0", false },
        { ">1.1.0-0", "1.0.0-0", false },
        { ">1.1.0-0", "0.0.0", false },
        { ">1.1.0-0", "0.0.0-0", false },
        { ">1.0.1", "2.0.0", true },
        { ">1.0.1", "2.0.0-0", true },
        { ">1.0.1", "1.1.0", true },
        { ">1.0.1", "1.1.0-0", true },
        { ">1.0.1", "1.0.1", false },
        { ">1.0.1", "1.0.1-0", false },
        { ">1.0.1", "1.0.0", false },
        { ">1.0.1", "1.0.0-0", false },
        { ">1.0.1", "0.0.0", false },
        { ">1.0.1", "0.0.0-0", false },
        { ">1.0.1-0", "2.0.0", true },
        { ">1.0.1-0", "2.0.0-0", true },
        { ">1.0.1-0", "1.1.0", true },
        { ">1.0.1-0", "1.1.0-0", true },
        { ">1.0.1-0", "1.0.1", true },
        { ">1.0.1-0", "1.0.1-0", false },
        { ">1.0.1-0", "1.0.0", false },
        { ">1.0.1-0", "1.0.0-0", false },
        { ">1.0.1-0", "0.0.0", false },
        { ">1.0.1-0", "0.0.0-0", false },
        { ">1.0.0", "2.0.0", true },
        { ">1.0.0", "2.0.0-0", true },
        { ">1.0.0", "1.1.0", true },
        { ">1.0.0", "1.1.0-0", true },
        { ">1.0.0", "1.0.1", true },
        { ">1.0.0", "1.0.1-0", true },
        { ">1.0.0", "1.0.0", false },
        { ">1.0.0", "1.0.0-0", false },
        { ">1.0.0", "0.0.0", false },
        { ">1.0.0", "0.0.0-0", false },
        { ">1.0.0-0", "2.0.0", true },
        { ">1.0.0-0", "2.0.0-0", true },
        { ">1.0.0-0", "1.1.0", true },
        { ">1.0.0-0", "1.1.0-0", true },
        { ">1.0.0-0", "1.0.1", true },
        { ">1.0.0-0", "1.0.1-0", true },
        { ">1.0.0-0", "1.0.0", true },
        { ">1.0.0-0", "1.0.0-0", false },
        { ">1.0.0-0", "0.0.0", false },
        { ">1.0.0-0", "0.0.0-0", false },

        // >= wildcard major (matches everything)
        { ">=*", "2.0.0", true },
        { ">=*", "2.0.0-0", true },
        { ">=*", "1.1.0", true },
        { ">=*", "1.1.0-0", true },
        { ">=*", "1.0.1", true },
        { ">=*", "1.0.1-0", true },
        { ">=*", "1.0.0", true },
        { ">=*", "1.0.0-0", true },
        { ">=*", "0.0.0", true },
        { ">=*", "0.0.0-0", true },

        // >= wildcard minor
        { ">=1", "2.0.0", true },
        { ">=1", "2.0.0-0", true },
        { ">=1", "1.1.0", true },
        { ">=1", "1.1.0-0", true },
        { ">=1", "1.0.1", true },
        { ">=1", "1.0.1-0", true },
        { ">=1", "1.0.0", true },
        { ">=1", "1.0.0-0", true },
        { ">=1", "0.0.0", false },
        { ">=1", "0.0.0-0", false },

        // >= wildcard patch
        { ">=1.1", "2.0.0", true },
        { ">=1.1", "2.0.0-0", true },
        { ">=1.1", "1.1.0", true },
        { ">=1.1", "1.1.0-0", true },
        { ">=1.1", "1.0.1", false },
        { ">=1.1", "1.0.1-0", false },
        { ">=1.1", "1.0.0", false },
        { ">=1.1", "1.0.0-0", false },
        { ">=1.1", "0.0.0", false },
        { ">=1.1", "0.0.0-0", false },
        { ">=1.0", "2.0.0", true },
        { ">=1.0", "2.0.0-0", true },
        { ">=1.0", "1.1.0", true },
        { ">=1.0", "1.1.0-0", true },
        { ">=1.0", "1.0.1", true },
        { ">=1.0", "1.0.1-0", true },
        { ">=1.0", "1.0.0", true },
        { ">=1.0", "1.0.0-0", true },
        { ">=1.0", "0.0.0", false },
        { ">=1.0", "0.0.0-0", false },

        // >= exact
        { ">=1.1.0", "2.0.0", true },
        { ">=1.1.0", "2.0.0-0", true },
        { ">=1.1.0", "1.1.0", true },
        { ">=1.1.0", "1.1.0-0", false },
        { ">=1.1.0", "1.0.1", false },
        { ">=1.1.0", "1.0.1-0", false },
        { ">=1.1.0", "1.0.0", false },
        { ">=1.1.0", "1.0.0-0", false },
        { ">=1.1.0", "0.0.0", false },
        { ">=1.1.0", "0.0.0-0", false },
        { ">=1.1.0-0", "2.0.0", true },
        { ">=1.1.0-0", "2.0.0-0", true },
        { ">=1.1.0-0", "1.1.0", true },
        { ">=1.1.0-0", "1.1.0-0", true },
        { ">=1.1.0-0", "1.0.1", false },
        { ">=1.1.0-0", "1.0.1-0", false },
        { ">=1.1.0-0", "1.0.0", false },
        { ">=1.1.0-0", "1.0.0-0", false },
        { ">=1.1.0-0", "0.0.0", false },
        { ">=1.1.0-0", "0.0.0-0", false },
        { ">=1.0.1", "2.0.0", true },
        { ">=1.0.1", "2.0.0-0", true },
        { ">=1.0.1", "1.1.0", true },
        { ">=1.0.1", "1.1.0-0", true },
        { ">=1.0.1", "1.0.1", true },
        { ">=1.0.1", "1.0.1-0", false },
        { ">=1.0.1", "1.0.0", false },
        { ">=1.0.1", "1.0.0-0", false },
        { ">=1.0.1", "0.0.0", false },
        { ">=1.0.1", "0.0.0-0", false },
        { ">=1.0.1-0", "2.0.0", true },
        { ">=1.0.1-0", "2.0.0-0", true },
        { ">=1.0.1-0", "1.1.0", true },
        { ">=1.0.1-0", "1.1.0-0", true },
        { ">=1.0.1-0", "1.0.1", true },
        { ">=1.0.1-0", "1.0.1-0", true },
        { ">=1.0.1-0", "1.0.0", false },
        { ">=1.0.1-0", "1.0.0-0", false },
        { ">=1.0.1-0", "0.0.0", false },
        { ">=1.0.1-0", "0.0.0-0", false },
        { ">=1.0.0", "2.0.0", true },
        { ">=1.0.0", "2.0.0-0", true },
        { ">=1.0.0", "1.1.0", true },
        { ">=1.0.0", "1.1.0-0", true },
        { ">=1.0.0", "1.0.1", true },
        { ">=1.0.0", "1.0.1-0", true },
        { ">=1.0.0", "1.0.0", true },
        { ">=1.0.0", "1.0.0-0", false },
        { ">=1.0.0", "0.0.0", false },
        { ">=1.0.0", "0.0.0-0", false },
        { ">=1.0.0-0", "2.0.0", true },
        { ">=1.0.0-0", "2.0.0-0", true },
        { ">=1.0.0-0", "1.1.0", true },
        { ">=1.0.0-0", "1.1.0-0", true },
        { ">=1.0.0-0", "1.0.1", true },
        { ">=1.0.0-0", "1.0.1-0", true },
        { ">=1.0.0-0", "1.0.0", true },
        { ">=1.0.0-0", "1.0.0-0", true },
        { ">=1.0.0-0", "0.0.0", false },
        { ">=1.0.0-0", "0.0.0-0", false },

        // < wildcard major (matches nothing)
        { "<*", "2.0.0", false },
        { "<*", "2.0.0-0", false },
        { "<*", "1.1.0", false },
        { "<*", "1.1.0-0", false },
        { "<*", "1.0.1", false },
        { "<*", "1.0.1-0", false },
        { "<*", "1.0.0", false },
        { "<*", "1.0.0-0", false },
        { "<*", "0.0.0", false },
        { "<*", "0.0.0-0", false },

        // < wildcard minor
        { "<1", "2.0.0", false },
        { "<1", "2.0.0-0", false },
        { "<1", "1.1.0", false },
        { "<1", "1.1.0-0", false },
        { "<1", "1.0.1", false },
        { "<1", "1.0.1-0", false },
        { "<1", "1.0.0", false },
        { "<1", "1.0.0-0", false },
        { "<1", "0.0.0", true },
        { "<1", "0.0.0-0", true },

        // < wildcard patch
        { "<1.1", "2.0.0", false },
        { "<1.1", "2.0.0-0", false },
        { "<1.1", "1.1.0", false },
        { "<1.1", "1.1.0-0", false },
        { "<1.1", "1.0.1", true },
        { "<1.1", "1.0.1-0", true },
        { "<1.1", "1.0.0", true },
        { "<1.1", "1.0.0-0", true },
        { "<1.1", "0.0.0", true },
        { "<1.1", "0.0.0-0", true },
        { "<1.0", "2.0.0", false },
        { "<1.0", "2.0.0-0", false },
        { "<1.0", "1.1.0", false },
        { "<1.0", "1.1.0-0", false },
        { "<1.0", "1.0.1", false },
        { "<1.0", "1.0.1-0", false },
        { "<1.0", "1.0.0", false },
        { "<1.0", "1.0.0-0", false },
        { "<1.0", "0.0.0", true },
        { "<1.0", "0.0.0-0", true },

        // < exact
        { "<1.1.0", "2.0.0", false },
        { "<1.1.0", "2.0.0-0", false },
        { "<1.1.0", "1.1.0", false },
        { "<1.1.0", "1.1.0-0", true },
        { "<1.1.0", "1.0.1", true },
        { "<1.1.0", "1.0.1-0", true },
        { "<1.1.0", "1.0.0", true },
        { "<1.1.0", "1.0.0-0", true },
        { "<1.1.0", "0.0.0", true },
        { "<1.1.0", "0.0.0-0", true },
        { "<1.1.0-0", "2.0.0", false },
        { "<1.1.0-0", "2.0.0-0", false },
        { "<1.1.0-0", "1.1.0", false },
        { "<1.1.0-0", "1.1.0-0", false },
        { "<1.1.0-0", "1.0.1", true },
        { "<1.1.0-0", "1.0.1-0", true },
        { "<1.1.0-0", "1.0.0", true },
        { "<1.1.0-0", "1.0.0-0", true },
        { "<1.1.0-0", "0.0.0", true },
        { "<1.1.0-0", "0.0.0-0", true },
        { "<1.0.1", "2.0.0", false },
        { "<1.0.1", "2.0.0-0", false },
        { "<1.0.1", "1.1.0", false },
        { "<1.0.1", "1.1.0-0", false },
        { "<1.0.1", "1.0.1", false },
        { "<1.0.1", "1.0.1-0", true },
        { "<1.0.1", "1.0.0", true },
        { "<1.0.1", "1.0.0-0", true },
        { "<1.0.1", "0.0.0", true },
        { "<1.0.1", "0.0.0-0", true },
        { "<1.0.1-0", "2.0.0", false },
        { "<1.0.1-0", "2.0.0-0", false },
        { "<1.0.1-0", "1.1.0", false },
        { "<1.0.1-0", "1.1.0-0", false },
        { "<1.0.1-0", "1.0.1", false },
        { "<1.0.1-0", "1.0.1-0", false },
        { "<1.0.1-0", "1.0.0", true },
        { "<1.0.1-0", "1.0.0-0", true },
        { "<1.0.1-0", "0.0.0", true },
        { "<1.0.1-0", "0.0.0-0", true },
        { "<1.0.0", "2.0.0", false },
        { "<1.0.0", "2.0.0-0", false },
        { "<1.0.0", "1.1.0", false },
        { "<1.0.0", "1.1.0-0", false },
        { "<1.0.0", "1.0.1", false },
        { "<1.0.0", "1.0.1-0", false },
        { "<1.0.0", "1.0.0", false },
        { "<1.0.0", "1.0.0-0", true },
        { "<1.0.0", "0.0.0", true },
        { "<1.0.0", "0.0.0-0", true },
        { "<1.0.0-0", "2.0.0", false },
        { "<1.0.0-0", "2.0.0-0", false },
        { "<1.0.0-0", "1.1.0", false },
        { "<1.0.0-0", "1.1.0-0", false },
        { "<1.0.0-0", "1.0.1", false },
        { "<1.0.0-0", "1.0.1-0", false },
        { "<1.0.0-0", "1.0.0", false },
        { "<1.0.0-0", "1.0.0-0", false },
        { "<1.0.0-0", "0.0.0", true },
        { "<1.0.0-0", "0.0.0-0", true },

        // <= wildcard major (matches everything)
        { "<=*", "2.0.0", true },
        { "<=*", "2.0.0-0", true },
        { "<=*", "1.1.0", true },
        { "<=*", "1.1.0-0", true },
        { "<=*", "1.0.1", true },
        { "<=*", "1.0.1-0", true },
        { "<=*", "1.0.0", true },
        { "<=*", "1.0.0-0", true },
        { "<=*", "0.0.0", true },
        { "<=*", "0.0.0-0", true },

        // <= wildcard minor
        { "<=1", "2.0.0", false },
        { "<=1", "2.0.0-0", false },
        { "<=1", "1.1.0", true },
        { "<=1", "1.1.0-0", true },
        { "<=1", "1.0.1", true },
        { "<=1", "1.0.1-0", true },
        { "<=1", "1.0.0", true },
        { "<=1", "1.0.0-0", true },
        { "<=1", "0.0.0", true },
        { "<=1", "0.0.0-0", true },

        // <= wildcard patch
        { "<=1.1", "2.0.0", false },
        { "<=1.1", "2.0.0-0", false },
        { "<=1.1", "1.1.0", true },
        { "<=1.1", "1.1.0-0", true },
        { "<=1.1", "1.0.1", true },
        { "<=1.1", "1.0.1-0", true },
        { "<=1.1", "1.0.0", true },
        { "<=1.1", "1.0.0-0", true },
        { "<=1.1", "0.0.0", true },
        { "<=1.1", "0.0.0-0", true },
        { "<=1.0", "2.0.0", false },
        { "<=1.0", "2.0.0-0", false },
        { "<=1.0", "1.1.0", false },
        { "<=1.0", "1.1.0-0", false },
        { "<=1.0", "1.0.1", true },
        { "<=1.0", "1.0.1-0", true },
        { "<=1.0", "1.0.0", true },
        { "<=1.0", "1.0.0-0", true },
        { "<=1.0", "0.0.0", true },
        { "<=1.0", "0.0.0-0", true },

        // <= exact
        { "<=1.1.0", "2.0.0", false },
        { "<=1.1.0", "2.0.0-0", false },
        { "<=1.1.0", "1.1.0", true },
        { "<=1.1.0", "1.1.0-0", true },
        { "<=1.1.0", "1.0.1", true },
        { "<=1.1.0", "1.0.1-0", true },
        { "<=1.1.0", "1.0.0", true },
        { "<=1.1.0", "1.0.0-0", true },
        { "<=1.1.0", "0.0.0", true },
        { "<=1.1.0", "0.0.0-0", true },
        { "<=1.1.0-0", "2.0.0", false },
        { "<=1.1.0-0", "2.0.0-0", false },
        { "<=1.1.0-0", "1.1.0", false },
        { "<=1.1.0-0", "1.1.0-0", true },
        { "<=1.1.0-0", "1.0.1", true },
        { "<=1.1.0-0", "1.0.1-0", true },
        { "<=1.1.0-0", "1.0.0", true },
        { "<=1.1.0-0", "1.0.0-0", true },
        { "<=1.1.0-0", "0.0.0", true },
        { "<=1.1.0-0", "0.0.0-0", true },
        { "<=1.0.1", "2.0.0", false },
        { "<=1.0.1", "2.0.0-0", false },
        { "<=1.0.1", "1.1.0", false },
        { "<=1.0.1", "1.1.0-0", false },
        { "<=1.0.1", "1.0.1", true },
        { "<=1.0.1", "1.0.1-0", true },
        { "<=1.0.1", "1.0.0", true },
        { "<=1.0.1", "1.0.0-0", true },
        { "<=1.0.1", "0.0.0", true },
        { "<=1.0.1", "0.0.0-0", true },
        { "<=1.0.1-0", "2.0.0", false },
        { "<=1.0.1-0", "2.0.0-0", false },
        { "<=1.0.1-0", "1.1.0", false },
        { "<=1.0.1-0", "1.1.0-0", false },
        { "<=1.0.1-0", "1.0.1", false },
        { "<=1.0.1-0", "1.0.1-0", true },
        { "<=1.0.1-0", "1.0.0", true },
        { "<=1.0.1-0", "1.0.0-0", true },
        { "<=1.0.1-0", "0.0.0", true },
        { "<=1.0.1-0", "0.0.0-0", true },
        { "<=1.0.0", "2.0.0", false },
        { "<=1.0.0", "2.0.0-0", false },
        { "<=1.0.0", "1.1.0", false },
        { "<=1.0.0", "1.1.0-0", false },
        { "<=1.0.0", "1.0.1", false },
        { "<=1.0.0", "1.0.1-0", false },
        { "<=1.0.0", "1.0.0", true },
        { "<=1.0.0", "1.0.0-0", true },
        { "<=1.0.0", "0.0.0", true },
        { "<=1.0.0", "0.0.0-0", true },
        { "<=1.0.0-0", "2.0.0", false },
        { "<=1.0.0-0", "2.0.0-0", false },
        { "<=1.0.0-0", "1.1.0", false },
        { "<=1.0.0-0", "1.1.0-0", false },
        { "<=1.0.0-0", "1.0.1", false },
        { "<=1.0.0-0", "1.0.1-0", false },
        { "<=1.0.0-0", "1.0.0", false },
        { "<=1.0.0-0", "1.0.0-0", true },
        { "<=1.0.0-0", "0.0.0", true },
        { "<=1.0.0-0", "0.0.0-0", true },

        // https://github.com/microsoft/TypeScript/issues/50909
        { ">4.8", "4.9.0-beta", true },
        { ">=4.9", "4.9.0-beta", true },
        { "<4.9", "4.9.0-beta", false },
        { "<=4.8", "4.9.0-beta", false },
    };

    public static TheoryData<string, string, bool> ConjunctionCases => new()
    {
        { ">1.0.0 <2.0.0", "1.0.1", true },
        { ">1.0.0 <2.0.0", "2.0.0", false },
        { ">1.0.0 <2.0.0", "1.0.0", false },
        { ">1 >2", "3.0.0", true },
    };

    public static TheoryData<string, string, bool> DisjunctionCases => new()
    {
        { ">1.0.0 || <1.0.0", "1.0.1", true },
        { ">1.0.0 || <1.0.0", "0.0.1", true },
        { ">1.0.0 || <1.0.0", "1.0.0", false },
        { ">1.0.0 || <1.0.0", "0.0.0", true },
        { ">=1.0.0 <2.0.0 || >=3.0.0 <4.0.0", "1.0.0", true },
        { ">=1.0.0 <2.0.0 || >=3.0.0 <4.0.0", "2.0.0", false },
        { ">=1.0.0 <2.0.0 || >=3.0.0 <4.0.0", "3.0.0", true },
    };

    public static TheoryData<string, string, bool> HyphenCases => new()
    {
        { "1.0.0 - 2.0.0", "1.0.0", true },
        { "1.0.0 - 2.0.0", "1.0.1", true },
        { "1.0.0 - 2.0.0", "2.0.0", true },
        { "1.0.0 - 2.0.0", "2.0.1", false },
        { "1.0.0 - 2.0.0", "0.9.9", false },
        { "1.0.0 - 2.0.0", "3.0.0", false },
    };

    public static TheoryData<string, string, bool> TildeCases => new()
    {
        { "~0", "0.0.0", true },
        { "~0", "0.1.0", true },
        { "~0", "0.1.2", true },
        { "~0", "0.1.9", true },
        { "~0", "1.0.0", false },
        { "~0.1", "0.1.0", true },
        { "~0.1", "0.1.2", true },
        { "~0.1", "0.1.9", true },
        { "~0.1", "0.2.0", false },
        { "~0.1.2", "0.1.2", true },
        { "~0.1.2", "0.1.9", true },
        { "~0.1.2", "0.2.0", false },
        { "~1.0.0", "1.0.0", true },
        { "~1.0.0", "1.0.1", true },
        { "~1", "1.0.0", true },
        { "~1", "1.2.0", true },
        { "~1", "1.2.3", true },
        { "~1", "0.0.0", false },
        { "~1", "2.0.0", false },
        { "~1.2", "1.2.0", true },
        { "~1.2", "1.2.3", true },
        { "~1.2", "1.1.0", false },
        { "~1.2", "1.3.0", false },
        { "~1.2.3", "1.2.3", true },
        { "~1.2.3", "1.2.9", true },
        { "~1.2.3", "1.1.0", false },
        { "~1.2.3", "1.3.0", false },
    };

    public static TheoryData<string, string, bool> CaretCases => new()
    {
        { "^0", "0.0.0", true },
        { "^0", "0.1.0", true },
        { "^0", "0.9.0", true },
        { "^0", "0.1.2", true },
        { "^0", "0.1.9", true },
        { "^0", "1.0.0", false },
        { "^0.1", "0.1.0", true },
        { "^0.1", "0.1.2", true },
        { "^0.1", "0.1.9", true },
        { "^0.1.2", "0.1.2", true },
        { "^0.1.2", "0.1.9", true },
        { "^0.1.2", "0.0.0", false },
        { "^0.1.2", "0.2.0", false },
        { "^0.1.2", "1.0.0", false },
        { "^1", "1.0.0", true },
        { "^1", "1.2.0", true },
        { "^1", "1.2.3", true },
        { "^1", "1.9.0", true },
        { "^1", "0.0.0", false },
        { "^1", "2.0.0", false },
        { "^1.2", "1.2.0", true },
        { "^1.2", "1.2.3", true },
        { "^1.2", "1.9.0", true },
        { "^1.2", "1.1.0", false },
        { "^1.2", "2.0.0", false },
        { "^1.2.3", "1.2.3", true },
        { "^1.2.3", "1.9.0", true },
        { "^1.2.3", "1.2.2", false },
        { "^1.2.3", "2.0.0", false },
    };

    [Theory]
    [MemberData(nameof(WildcardStringCases))]
    public void TestWildcardsHaveSameString(string s1, string s2)
    {
        // Go 的子测试名：`<group>` 下的 `s1 == s2`。
        output.WriteLine($"{s1} == {s2}");

        Assert.True(TryParseVersionRange(s1, out var v1));
        Assert.True(TryParseVersionRange(s2, out var v2));
        Assert.Equal(v1.ToString(), v2.ToString());
    }

    [Theory]
    [MemberData(nameof(VersionRangesCases))]
    public void TestVersionRanges(string versionRangeString, string versionText, bool expected)
    {
        AssertRangeTest("versionRanges", versionRangeString, versionText, expected);
    }

    [Theory]
    [MemberData(nameof(ComparatorsCases))]
    public void TestComparatorsOfVersionRanges(string rangeText, string versionText, bool expected)
    {
        AssertRangeTest("comparators", rangeText, versionText, expected);
    }

    [Theory]
    [MemberData(nameof(ConjunctionCases))]
    public void TestConjunctionsOfVersionRanges(string rangeText, string versionText, bool expected)
    {
        AssertRangeTest("conjunctions", rangeText, versionText, expected);
    }

    [Theory]
    [MemberData(nameof(DisjunctionCases))]
    public void TestDisjunctionsOfVersionRanges(string rangeText, string versionText, bool expected)
    {
        AssertRangeTest("disjunctions", rangeText, versionText, expected);
    }

    [Theory]
    [MemberData(nameof(HyphenCases))]
    public void TestHyphensOfVersionRanges(string rangeText, string versionText, bool expected)
    {
        AssertRangeTest("hyphens", rangeText, versionText, expected);
    }

    [Theory]
    [MemberData(nameof(TildeCases))]
    public void TestTildesOfVersionRanges(string rangeText, string versionText, bool expected)
    {
        AssertRangeTest("tilde", rangeText, versionText, expected);
    }

    [Theory]
    [MemberData(nameof(CaretCases))]
    public void TestCaretsOfVersionRanges(string rangeText, string versionText, bool expected)
    {
        AssertRangeTest("caret", rangeText, versionText, expected);
    }

    // Go: assertRangesGoodBad / assertRangeTest —— 两者做的断言相同：range 与 version 都必须解析成功，
    // 然后 `versionRange.Test(&version) == inRange`。
    private void AssertRangeTest(string name, string rangeText, string versionText, bool inRange)
    {
        output.WriteLine($"{name} (version {versionText} in range {rangeText}) == {inRange}");

        Assert.True(TryParseVersionRange(rangeText, out var versionRange));
        Assert.True(TryParseVersion(versionText, out var version));
        Assert.Equal(inRange, versionRange.Test(version));
    }
}
