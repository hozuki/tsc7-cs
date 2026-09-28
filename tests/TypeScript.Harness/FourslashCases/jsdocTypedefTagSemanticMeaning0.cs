// ported from: internal/fourslash/tests/jsdocTypedefTagSemanticMeaning0_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagSemanticMeaning0Case
{
    public static void TestJsdocTypedefTagSemanticMeaning0(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\n/** /*1*/@typedef {number} /*2*/T */\n/*3*/const /*4*/T = 1;\n/** @type {/*5*/T} */\nconst n = /*6*/T;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6");
            }
            finally
            {
                done();
            }
        });
    }

}
