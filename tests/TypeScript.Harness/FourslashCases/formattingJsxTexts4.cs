// ported from: internal/fourslash/tests/formattingJsxTexts4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingJsxTexts4Case
{
    public static void TestFormattingJsxTexts4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\nfunction foo() {\nconst a = <ns: foobar   x : test1   x :test2=\"string\"  x:test3={true?1:0}  />;\n\nreturn a;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "function foo() {\n    const a = <ns:foobar x:test1 x:test2=\"string\" x:test3={true ? 1 : 0} />;\n\n    return a;\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
