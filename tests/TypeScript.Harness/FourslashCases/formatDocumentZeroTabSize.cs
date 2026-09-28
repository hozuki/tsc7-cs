// ported from: internal/fourslash/tests/formatDocumentZeroTabSize_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatDocumentZeroTabSizeCase
{
    public static void TestFormatDocumentZeroTabSize(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo() {\n    if (true) {\n        var x = 1;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts = f.GetOptions();
                opts!.FormatCodeSettings!.TabSize = 0;
                opts!.FormatCodeSettings!.IndentSize = 0;
                opts!.FormatCodeSettings!.ConvertTabsToSpaces = TRI.TSTrue;
                f.Configure(t, opts);
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "function foo() {\nif (true) {\nvar x = 1;\n}\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
