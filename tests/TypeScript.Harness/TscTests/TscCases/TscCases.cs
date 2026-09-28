// 非 Go 源文件：`tsc_test.go` / `showconfig_test.go` / `tscwatch_test.go` 的用例聚合（Go 侧一个 `TestXxx`
// 函数 = 这里的一个 `XxxCases` 类）。
//
// 每加一个 Go 测试函数的移植，就往 <see cref="All"/> 里挂一行；名字与 Go 的函数名逐字对应，
// 这样 `--case TestTscCommandline` 的过滤口径与 `go test -run TestTscCommandline` 一致。
//
// TscCommandLineCases / TscMissingFilesCases 是**上一片人工精修**的产物（保留逐字节 Dedent 常量与偏离说明），
// 其余 `*Cases` 由 `artifacts/gen-tsccases.js` 生成（见各文件头）。生成器对这两个函数名有 skip 表，不会覆盖。
namespace TypeScript.Harness.TscTests;

using global::System.Collections.Generic;
using global::System.Linq;

internal static class TscCases
{
    /// <summary>Go: <c>go test ./internal/execute/tsctests -run 'TestTsc|TestForce|TestGenerate|TestType|TestShow|TestWatch'</c> 的用例并集。</summary>
    public static IEnumerable<TscCaseEntry> All() => new[]
    {
        TscCommandLineCases.All(),
        TscMissingFilesCases.All(),
        TscCompositeCases.All(),
        TscDeclarationEmitCases.All(),
        TscExtendsCases.All(),
        ForceConsistentCasingInFileNamesCases.All(),
        TscIgnoreConfigCases.All(),
        TscIncrementalCases.All(),
        TscLibraryResolutionCases.All(),
        TscListFilesOnlyCases.All(),
        TscModuleResolutionCases.All(),
        TscNoCheckCases.All(),
        TscNoEmitCases.All(),
        TscNoEmitOnErrorCases.All(),
        TscProjectReferencesCases.All(),
        TypeAcquisitionCases.All(),
        GenerateTraceCases.All(),
        TscContentMapperEmitCases.All(),
        TscContentMapperExplainFilesCases.All(),
        TscContentMapperOptionDiagnosticsCases.All(),
        TscContentMapperFailuresCases.All(),
        TscContentMapperSynthesizedCases.All(),
        ShowConfigCases.All(),
        WatchCases.All(),
        TscNoEmitWatchCases.All(),
    }.SelectMany(x => x);
}
