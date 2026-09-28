// ported from: internal/fourslash/tests/formattingInDestructuring5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingInDestructuring5Case
{
    public static void TestFormattingInDestructuring5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let a, b;\n/*1*/if (false)[a, b] = [1, 2];\n/*2*/if (true)        [a, b] = [1, 2];\n/*3*/var a = [1, 2, 3].map(num => num) [0];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "if (false) [a, b] = [1, 2];");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "if (true) [a, b] = [1, 2];");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "var a = [1, 2, 3].map(num => num)[0];");
            }
            finally
            {
                done();
            }
        });
    }

}
