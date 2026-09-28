// 非 Go 源文件：`tscbuild_test.go` 的用例聚合（Go 侧一个 `TestBuildXxx` 函数 = 这里的一个 `XxxCases` 类）。
//
// 目前为空：`--suite tscbuild` 已接好框架（baseline 根 `tsbuild/**`、`tsbuildWatch/**`），用例按
// artifacts/tsctests-plan.md §4 的切片 B 逐个追加。
namespace TypeScript.Harness.TscTests;

using global::System.Collections.Generic;

internal static class TscBuildCases
{
    /// <summary>Go: <c>go test ./internal/execute/tsctests -run 'TestBuild'</c> 的用例并集。</summary>
    public static IEnumerable<TscCaseEntry> All()
    {
        yield break;
    }
}
