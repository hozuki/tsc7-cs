// ported from: internal/fourslash/tests/indentationInJsx3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IndentationInJsx3Case
{
    public static void TestIndentationInJsx3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\nfunction foo() {\n   return (\n        <div>\nhello\ngoodbye\n        </div>\n    )\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCurrentFileContent(t, "function foo() {\n   return (\n        <div>\nhello\ngoodbye\n        </div>\n    )\n}");
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "function foo() {\n    return (\n        <div>\n            hello\n            goodbye\n        </div>\n    )\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
