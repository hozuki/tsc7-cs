// ported from: internal/fourslash/tests/syntacticClassificationsFunctionWithComments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntacticClassificationsFunctionWithCommentsCase
{
    public static void TestSyntacticClassificationsFunctionWithComments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/**\n * This is my function.\n * There are many like it, but this one is mine.\n */\nfunction myFunction(/* x */ x: any) {\n    var y = x ? x++ : ++x;\n}\n// end of file"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "function.declaration", Text = "myFunction" }, new F.SemanticToken { Type = "parameter.declaration", Text = "x" }, new F.SemanticToken { Type = "variable.declaration.local", Text = "y" }, new F.SemanticToken { Type = "parameter", Text = "x" }, new F.SemanticToken { Type = "parameter", Text = "x" }, new F.SemanticToken { Type = "parameter", Text = "x" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
