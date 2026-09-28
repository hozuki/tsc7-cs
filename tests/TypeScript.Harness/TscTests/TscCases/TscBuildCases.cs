// 非 Go 源文件：`tscbuild_test.go` 的用例聚合（Go 侧一个 `TestBuildXxx` 函数 = 这里的一个 `BuildXxxCases` 类）。
//
// 全部由 `artifacts/gen-tsccases.js` 生成（见各文件头）；`--suite tscbuild` 与 `go test -run 'TestBuild'` 同口径。
// 未移植：`TestBuildOutputPaths`（其 `t.Run("GetOutputFileNames/…")` 子测试用了 `tsoptions.GetParsedCommandLineOfConfigFile`
// + `assert.DeepEqual` 的真实断言辅助，不属于"用例表"形态，见本片报告）。
namespace TypeScript.Harness.TscTests;

using global::System.Collections.Generic;
using global::System.Linq;

internal static class TscBuildCases
{
    /// <summary>Go: <c>go test ./internal/execute/tsctests -run TestBuild</c> 的用例并集。</summary>
    public static IEnumerable<TscCaseEntry> All() => new[]
    {
        BuildCommandLineCases.All(),
        BuildContentMapperIdentityCases.All(),
        BuildContentMapperOptionDiagnosticsCases.All(),
        BuildCleanCases.All(),
        BuildConfigFileErrorsCases.All(),
        BuildDemoProjectCases.All(),
        BuildEmitDeclarationOnlyCases.All(),
        BuildFileDeleteCases.All(),
        BuildDependencyUpdateCases.All(),
        BuildInferredTypeFromTransitiveModuleCases.All(),
        BuildInferredTypeFromMonorepoReferenceCases.All(),
        BuildDeclarationEmitForReferencedProjectTypesSubpathCases.All(),
        BuildJavascriptProjectEmitCases.All(),
        BuildLateBoundSymbolCases.All(),
        BuildModuleSpecifiersCases.All(),
        BuildProgramUpdatesCases.All(),
        BuildProjectsBuildingCases.All(),
        BuildProjectReferenceWithRootDirInParentCases.All(),
        BuildReexportCases.All(),
        BuildResolveJsonModuleCases.All(),
        BuildRootsCases.All(),
        BuildSampleCases.All(),
        BuildTransitiveReferencesCases.All(),
        BuildSolutionProjectCases.All(),
        BuildProjectReferenceRedirectWithMultipleSubProjectsCases.All(),
    }.SelectMany(x => x);
}
