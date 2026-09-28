// ported from: internal/fourslash/tests/syntacticClassificationForJSDocTemplateTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntacticClassificationForJSDocTemplateTagCase
{
    public static void TestSyntacticClassificationForJSDocTemplateTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** @template T baring strait */\nfunction ident<T>: T {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "function.declaration", Text = "ident" }, new F.SemanticToken { Type = "typeParameter.declaration", Text = "T" }, new F.SemanticToken { Type = "typeParameter", Text = "T" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
