// ported from: internal/fourslash/tests/formattingMultilineTemplateLiterals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingMultilineTemplateLiteralsCase
{
    public static void TestFormattingMultilineTemplateLiterals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/new Error(`Failed to expand glob: ${projectSpec.filesGlob}\n/*2*/                at projectPath : ${projectFile}\n/*3*/                with error: ${ex.message}`)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "new Error(`Failed to expand glob: ${projectSpec.filesGlob}");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "                at projectPath : ${projectFile}");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "                with error: ${ex.message}`)");
            }
            finally
            {
                done();
            }
        });
    }

}
