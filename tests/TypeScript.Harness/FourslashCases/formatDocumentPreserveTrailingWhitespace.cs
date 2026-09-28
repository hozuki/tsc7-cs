// ported from: internal/fourslash/tests/formatDocumentPreserveTrailingWhitespace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatDocumentPreserveTrailingWhitespaceCase
{
    public static void TestFormatDocumentPreserveTrailingWhitespace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nvar a;     \nvar b     \n     \n//     \nfunction b(){     \n    while(true){     \n    }     \n}     \n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts233 = f.GetOptions();
                opts233!.FormatCodeSettings!.TrimTrailingWhitespace = TRI.TSFalse;
                f.Configure(t, opts233);
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nvar a;     \nvar b     \n     \n//     \nfunction b() {     \n    while (true) {     \n    }     \n}     \n");
            }
            finally
            {
                done();
            }
        });
    }

}
