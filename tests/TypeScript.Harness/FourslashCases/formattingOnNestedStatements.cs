// ported from: internal/fourslash/tests/formattingOnNestedStatements_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnNestedStatementsCase
{
    public static void TestFormattingOnNestedStatements(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("{\n/*1*/{\n/*3*/test\n}/*2*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatSelection(t, "1", "2");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "        test");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    }");
            }
            finally
            {
                done();
            }
        });
    }

}
