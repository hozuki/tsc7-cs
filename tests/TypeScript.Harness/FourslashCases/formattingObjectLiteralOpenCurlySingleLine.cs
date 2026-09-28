// ported from: internal/fourslash/tests/formattingObjectLiteralOpenCurlySingleLine_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingObjectLiteralOpenCurlySingleLineCase
{
    public static void TestFormattingObjectLiteralOpenCurlySingleLine(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nlet obj1 =\n{ x: 10 };\n\nlet obj2 =\n    // leading trivia\n{ y: 10 };\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nlet obj1 =\n    { x: 10 };\n\nlet obj2 =\n    // leading trivia\n    { y: 10 };\n");
            }
            finally
            {
                done();
            }
        });
    }

}
