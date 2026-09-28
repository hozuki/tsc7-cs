// ported from: internal/fourslash/tests/noCompletionListOnCommentsInsideObjectLiterals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NoCompletionListOnCommentsInsideObjectLiteralsCase
{
    public static void TestNoCompletionListOnCommentsInsideObjectLiterals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace ObjectLiterals {\n\tinterface MyPoint {\n\t\tx1: number;\n\t\ty1: number;\n\t}\n\n\tvar p1: MyPoint = {\n\t\t/* /*1*/ Comment /*2*/ */\n\t};\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), null!);
            }
            finally
            {
                done();
            }
        });
    }

}
