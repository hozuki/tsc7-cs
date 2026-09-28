// 非 Go 源文件：`tsc_test.go` 的用例聚合（Go 侧一个 `TestXxx` 函数 = 这里的一个 `XxxCases` 类）。
//
// 每加一个 Go 测试函数的移植，就往 <see cref="All"/> 里挂一行；名字与 Go 的函数名逐字对应，
// 这样 `--case TestTscCommandline` 的过滤口径与 `go test -run TestTscCommandline` 一致。
namespace TypeScript.Harness.TscTests;

using global::System.Collections.Generic;
using global::System.Linq;

internal static class TscCases
{
    /// <summary>Go: <c>go test ./internal/execute/tsctests -run 'TestTsc|TestForce|TestGenerate|TestType'</c> 的用例并集。</summary>
    public static IEnumerable<TscCaseEntry> All() => new[]
    {
        TscCommandLineCases.All(),
        TscMissingFilesCases.All(),
    }.SelectMany(x => x);
}
