// ported from: internal/fourslash/tests/formattingArrayLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingArrayLiteralCase
{
    public static void TestFormattingArrayLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/x= [];\ny = [\n/*2*/           1,\n/*3*/  2\n/*4*/ ];\n\nz = [[\n/*5*/  1,\n/*6*/             2\n/*7*/      ]  ];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "x = [];");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    1,");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    2");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "];");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    1,");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    2");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "]];");
            }
            finally
            {
                done();
            }
        });
    }

}
