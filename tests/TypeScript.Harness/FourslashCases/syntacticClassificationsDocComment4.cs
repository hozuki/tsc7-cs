// ported from: internal/fourslash/tests/syntacticClassificationsDocComment4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntacticClassificationsDocComment4Case
{
    public static void TestSyntacticClassificationsDocComment4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** @param {number} p1 */\nfunction foo(p1) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "function.declaration", Text = "foo" }, new F.SemanticToken { Type = "parameter.declaration", Text = "p1" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
