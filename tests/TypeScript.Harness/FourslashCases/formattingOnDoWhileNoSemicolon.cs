// ported from: internal/fourslash/tests/formattingOnDoWhileNoSemicolon_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnDoWhileNoSemicolonCase
{
    public static void TestFormattingOnDoWhileNoSemicolon(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*2*/do {\n/*3*/    for (var i = 0; i < 10; i++)\n/*4*/        i -= 2\n/*5*/        }/*1*/while (1 !== 1)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "\n");
                f.VerifyCurrentLineContent(t, "while (1 !== 1)");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "do {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    for (var i = 0; i < 10; i++)");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "        i -= 2");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
