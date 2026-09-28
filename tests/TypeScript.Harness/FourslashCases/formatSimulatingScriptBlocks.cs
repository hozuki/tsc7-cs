// ported from: internal/fourslash/tests/formatSimulatingScriptBlocks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSimulatingScriptBlocksCase
{
    public static void TestFormatSimulatingScriptBlocks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/* BEGIN EXTERNAL SOURCE */\n/*begin5*/\n                        var a = 1;\n                        alert(\"/*end5*//********//*begin4*/\");\n                    /*end4*/\n/* END EXTERNAL SOURCE */\n\n/* BEGIN EXTERNAL SOURCE */\n/*begin3*/\n                            var b = 1;\n\n                        var c = \"/*end3*//********//*begin2*/\";\n       var d = 1;\n\n            var e = \"/*end2*//********//*begin1*/\";\n            var f = 1;\n        /*end1*/\n/* END EXTERNAL SOURCE */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts640 = f.GetOptions();
                opts640!.FormatCodeSettings!.BaseIndentSize = 12;
                f.Configure(t, opts640);
                f.FormatSelection(t, "begin1", "end1");
                f.FormatSelection(t, "begin2", "end2");
                f.FormatSelection(t, "begin3", "end3");
                var opts794 = f.GetOptions();
                opts794!.FormatCodeSettings!.BaseIndentSize = 24;
                f.Configure(t, opts794);
                f.FormatSelection(t, "begin4", "end4");
                f.FormatSelection(t, "begin5", "end5");
                f.VerifyCurrentFileContent(t, "/* BEGIN EXTERNAL SOURCE */\n\n                        var a = 1;\n                        alert(\"/********/\");\n\n/* END EXTERNAL SOURCE */\n\n/* BEGIN EXTERNAL SOURCE */\n\n            var b = 1;\n\n            var c = \"/********/\";\n            var d = 1;\n\n            var e = \"/********/\";\n            var f = 1;\n\n/* END EXTERNAL SOURCE */");
            }
            finally
            {
                done();
            }
        });
    }

}
