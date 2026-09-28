// 非 Go 源文件：tsctests 的用例注册表（把 Go 的 `func TestXxx` + `testCases` 表映射成带名字的执行体）。
//
// 命名口径：registry 名 = `<GoTestFunc>/<scenario>/<subScenario>`，与 Go 的
// `go test -run 'TestTscCommandline/commandLine/<subScenario>'` 同形（scenario 是 `testCase.run(t, "…")` 的第二个参数）。
namespace TypeScript.Harness.TscTests;

using global::System;
using global::System.Collections.Generic;
using TypeScript.TestUtil;

/// <summary>registry 里的一个用例：名字（`--case` 过滤用）+ 执行体。</summary>
public sealed record TscCaseEntry(string Name, Action<IHarnessTest> Body);

/// <summary>tsctests 的用例注册表。</summary>
public static class TscCaseRegistry
{
    /// <summary>套件名 → 该套件的用例集合（`tsctests` = tsc_test.go/showconfig_test.go；`tscbuild` = tscbuild_test.go）。</summary>
    public static IEnumerable<TscCaseEntry> ForSuite(string suite) => suite switch
    {
        "tsctests" => TscCases.All(),
        "tscbuild" => TscBuildCases.All(),
        _ => throw new ArgumentException("unknown tsc suite: " + suite),
    };

    /// <summary>某个套件是否已被 tsctests 入口接管。</summary>
    public static bool IsTscSuite(string suite) => suite is "tsctests" or "tscbuild";

    /// <summary>把一组 <c>TscInput</c> 变成 registry 条目（`GetBaselineSubFolder` 由每个用例的参数决定）。</summary>
    public static IEnumerable<TscCaseEntry> FromInputs(string testName, string scenario, IEnumerable<TscInput> inputs)
    {
        foreach (var input in inputs)
        {
            var captured = input;
            yield return new TscCaseEntry(
                $"{testName}/{scenario}/{captured.SubScenario}",
                t => captured.Run(t, scenario));
        }
    }
}
