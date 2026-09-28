// ported from: internal/fourslash/tests/formattingOnNestedDoWhileByEnter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnNestedDoWhileByEnterCase
{
    public static void TestFormattingOnNestedDoWhileByEnter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*2*/do{\n/*3*/do/*1*/{\n/*4*/do{\n/*5*/}while(a!==b)\n/*6*/}while(a!==b)\n/*7*/}while(a!==b)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "\n");
                f.VerifyCurrentLineContent(t, "    {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "do{");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    do");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "do{");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "}while(a!==b)");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "}while(a!==b)");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "}while(a!==b)");
            }
            finally
            {
                done();
            }
        });
    }

}
