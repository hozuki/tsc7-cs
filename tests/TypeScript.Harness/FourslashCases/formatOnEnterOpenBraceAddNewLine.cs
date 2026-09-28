// ported from: internal/fourslash/tests/formatOnEnterOpenBraceAddNewLine_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatOnEnterOpenBraceAddNewLineCase
{
    public static void TestFormatOnEnterOpenBraceAddNewLine(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("if(true) {/*0*/}\nif(false)/*1*/{\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts148 = f.GetOptions();
                opts148!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForControlBlocks = TRI.TSTrue;
                f.Configure(t, opts148);
                f.GoToMarker(t, "0");
                f.InsertLine(t, "");
                f.VerifyCurrentFileContent(t, "if (true)\n{\n}\nif(false){\n}");
                f.GoToMarker(t, "1");
                f.InsertLine(t, "");
                f.VerifyCurrentFileContent(t, "if (true)\n{\n}\nif (false)\n{\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
