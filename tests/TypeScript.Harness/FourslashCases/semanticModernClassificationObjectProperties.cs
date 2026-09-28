// ported from: internal/fourslash/tests/semanticModernClassificationObjectProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationObjectPropertiesCase
{
    public static void TestSemanticModernClassificationObjectProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let x = 1, y = 1;\nconst a1 = { e: 1 };\nvar a2 = { x };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable.declaration", Text = "x" }, new F.SemanticToken { Type = "variable.declaration", Text = "y" }, new F.SemanticToken { Type = "variable.declaration.readonly", Text = "a1" }, new F.SemanticToken { Type = "property.declaration", Text = "e" }, new F.SemanticToken { Type = "variable.declaration", Text = "a2" }, new F.SemanticToken { Type = "property.declaration", Text = "x" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
