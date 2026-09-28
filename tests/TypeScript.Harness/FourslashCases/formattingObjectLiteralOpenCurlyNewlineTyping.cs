// ported from: internal/fourslash/tests/formattingObjectLiteralOpenCurlyNewlineTyping_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingObjectLiteralOpenCurlyNewlineTypingCase
{
    public static void TestFormattingObjectLiteralOpenCurlyNewlineTyping(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nvar varName =/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "\n{");
                f.VerifyCurrentFileContent(t, "\nvar varName =\n    {\n");
                f.Insert(t, "\na: 1");
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nvar varName =\n{\n    a: 1\n");
                f.Insert(t, "\n};");
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nvar varName =\n{\n    a: 1\n};\n");
            }
            finally
            {
                done();
            }
        });
    }

}
