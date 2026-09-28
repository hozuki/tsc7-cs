// ported from: internal/fourslash/tests/formatTemplateStringOnPaste_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatTemplateStringOnPasteCase
{
    public static void TestFormatTemplateStringOnPaste(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const x = `${0}/*0*/abc/*1*/`;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatSelection(t, "0", "1");
                f.VerifyCurrentFileContent(t, "const x = `${0}abc`;");
            }
            finally
            {
                done();
            }
        });
    }

}
