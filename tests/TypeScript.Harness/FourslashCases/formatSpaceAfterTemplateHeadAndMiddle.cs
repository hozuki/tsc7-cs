// ported from: internal/fourslash/tests/formatSpaceAfterTemplateHeadAndMiddle_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSpaceAfterTemplateHeadAndMiddleCase
{
    public static void TestFormatSpaceAfterTemplateHeadAndMiddle(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const a1 = `${1}${1}`;\nconst a2 = `\n    ${1}${1}\n`;\nconst a3 = `\n\n\n    ${1}${1}\n`;\nconst a4 = `\n\n    ${1}${1}\n\n`;\nconst a5 = `text ${1} text ${1} text`;\nconst a6 = `\n    text ${1}\n    text ${1}\n    text\n`;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts405 = f.GetOptions();
                opts405!.FormatCodeSettings!.InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces = TRI.TSTrue;
                f.Configure(t, opts405);
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "const a1 = `${ 1 }${ 1 }`;\n" + "const a2 = `\n" + "    ${ 1 }${ 1 }\n" + "`;\n" + "const a3 = `\n" + "\n" + "\n" + "    ${ 1 }${ 1 }\n" + "`;\n" + "const a4 = `\n" + "\n" + "    ${ 1 }${ 1 }\n" + "\n" + "`;\n" + "const a5 = `text ${ 1 } text ${ 1 } text`;\n" + "const a6 = `\n" + "    text ${ 1 }\n" + "    text ${ 1 }\n" + "    text\n" + "`;");
            }
            finally
            {
                done();
            }
        });
    }

}
