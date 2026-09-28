// ported from: internal/fourslash/tests/formattingOnCommaOperator_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnCommaOperatorCase
{
    public static void TestFormattingOnCommaOperator(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var v1 = ((1, 2, 3), 4, 5, (6, 7));/*1*/\nfunction f1() {\n    var a = 1;\n    return a, v1, a;/*2*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "var v1 = ((1, 2, 3), 4, 5, (6, 7));");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    return a, v1, a;");
            }
            finally
            {
                done();
            }
        });
    }

}
