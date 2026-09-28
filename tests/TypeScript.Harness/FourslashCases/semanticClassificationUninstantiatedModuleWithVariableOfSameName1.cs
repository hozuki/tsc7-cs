// ported from: internal/fourslash/tests/semanticClassificationUninstantiatedModuleWithVariableOfSameName1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationUninstantiatedModuleWithVariableOfSameName1Case
{
    public static void TestSemanticClassificationUninstantiatedModuleWithVariableOfSameName1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module /*0*/M {\n    interface /*1*/I {\n\n    }\n}\n\nvar M = { I: 10 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable", Text = "M" }, new F.SemanticToken { Type = "interface.declaration", Text = "I" }, new F.SemanticToken { Type = "variable.declaration", Text = "M" }, new F.SemanticToken { Type = "property.declaration", Text = "I" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
