// ported from: internal/fourslash/tests/semanticModernClassificationCallableVariables2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationCallableVariables2Case
{
    public static void TestSemanticModernClassificationCallableVariables2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import \"node\";\nvar fs = require(\"fs\")\nrequire.resolve('react');\nrequire.resolve.paths;\ninterface LanguageMode { getFoldingRanges?: (d: string) => number[]; };\nfunction (mode: LanguageMode | undefined) { if (mode && mode.getFoldingRanges) { return mode.getFoldingRanges('a'); }};\nfunction b(a: () => void) { a(); };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable.declaration", Text = "fs" }, new F.SemanticToken { Type = "interface.declaration", Text = "LanguageMode" }, new F.SemanticToken { Type = "method.declaration", Text = "getFoldingRanges" }, new F.SemanticToken { Type = "parameter.declaration", Text = "d" }, new F.SemanticToken { Type = "parameter.declaration", Text = "mode" }, new F.SemanticToken { Type = "interface", Text = "LanguageMode" }, new F.SemanticToken { Type = "parameter", Text = "mode" }, new F.SemanticToken { Type = "parameter", Text = "mode" }, new F.SemanticToken { Type = "method", Text = "getFoldingRanges" }, new F.SemanticToken { Type = "parameter", Text = "mode" }, new F.SemanticToken { Type = "method", Text = "getFoldingRanges" }, new F.SemanticToken { Type = "function.declaration", Text = "b" }, new F.SemanticToken { Type = "function.declaration", Text = "a" }, new F.SemanticToken { Type = "function", Text = "a" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
