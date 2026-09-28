// ported from: internal/fourslash/tests/formattingObjectLiteralOpenCurlyNewline_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingObjectLiteralOpenCurlyNewlineCase
{
    public static void TestFormattingObjectLiteralOpenCurlyNewline(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nvar clear =\n{\n    outerKey:\n    {\n        innerKey: 1,\n        innerKey2:\n            2\n    }\n};\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nvar clear =\n{\n    outerKey:\n    {\n        innerKey: 1,\n        innerKey2:\n            2\n    }\n};\n");
                var opts444 = f.GetOptions();
                opts444!.FormatCodeSettings!.IndentMultiLineObjectLiteralBeginningOnBlankLine = TRI.TSTrue;
                f.Configure(t, opts444);
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nvar clear =\n    {\n        outerKey:\n            {\n                innerKey: 1,\n                innerKey2:\n                    2\n            }\n    };\n");
            }
            finally
            {
                done();
            }
        });
    }

}
