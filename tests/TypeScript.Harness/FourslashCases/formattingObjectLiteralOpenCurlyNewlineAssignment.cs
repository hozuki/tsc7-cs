// ported from: internal/fourslash/tests/formattingObjectLiteralOpenCurlyNewlineAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingObjectLiteralOpenCurlyNewlineAssignmentCase
{
    public static void TestFormattingObjectLiteralOpenCurlyNewlineAssignment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nvar obj = {};\nobj =\n{\n    prop: 3\n};\n \nvar obj2 = obj ||\n{\n    prop: 0\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nvar obj = {};\nobj =\n{\n    prop: 3\n};\n\nvar obj2 = obj ||\n{\n    prop: 0\n}\n");
                var opts400 = f.GetOptions();
                opts400!.FormatCodeSettings!.IndentMultiLineObjectLiteralBeginningOnBlankLine = TRI.TSTrue;
                f.Configure(t, opts400);
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nvar obj = {};\nobj =\n    {\n        prop: 3\n    };\n\nvar obj2 = obj ||\n    {\n        prop: 0\n    }\n");
            }
            finally
            {
                done();
            }
        });
    }

}
