// ported from: internal/fourslash/tests/syntacticClassificationsTemplates1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntacticClassificationsTemplates1Case
{
    public static void TestSyntacticClassificationsTemplates1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var v = 10e0;\nvar x = {\n    p1: `hello world`,\n    p2: `goodbye ${0} cruel ${0} world`,\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable.declaration", Text = "v" }, new F.SemanticToken { Type = "variable.declaration", Text = "x" }, new F.SemanticToken { Type = "property.declaration", Text = "p1" }, new F.SemanticToken { Type = "property.declaration", Text = "p2" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
