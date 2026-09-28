// ported from: internal/fourslash/tests/formatDocumentWithTrivia_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatDocumentWithTriviaCase
{
    public static void TestFormatDocumentWithTrivia(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("  \n// 1 below   \n    \n// 2 above   \n    \nlet x;\n  \n// abc\n  \nlet y;\n  \n// 3 above\n   \nwhile (true) {\n    while (true) {\n    }\n      \n    // 4 above   \n}\n  \n// 5 above  \n   \n   "u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\n// 1 below   \n\n// 2 above   \n\nlet x;\n\n// abc\n\nlet y;\n\n// 3 above\n\nwhile (true) {\n    while (true) {\n    }\n\n    // 4 above   \n}\n\n// 5 above  \n\n");
            }
            finally
            {
                done();
            }
        });
    }

}
