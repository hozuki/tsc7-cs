// ported from: internal/testrunner/compiler_runner.go:28-35（本文件目前只落 TestCaseParser 需要的包级 var）
//
// 说明（**M7 未完成**）：`internal/testrunner/compiler_runner.go`（编译器用例 runner，依赖
// `internal/compiler`/`internal/execute`/`internal/testutil/*`）整体在 M7 才移植；本波只落
// `test_case_parser.go` 真正引用的那个包级 var `srcFolder`，其余（CompilerTestType 常量、
// `runCompilerTest`/`enumerateTestFiles`… ）随 M7 的 harness 一起补，`docs/checker-plan.md` §3.1 已登记。
namespace TypeScript.TestRunner;

public static partial class TestRunner
{
    /// <summary>Go: <c>var srcFolder = "/.src"</c>（compiler_runner.go:34，Posix 风格的被测源码根）。</summary>
    internal static readonly string SrcFolder = "/.src";
}
