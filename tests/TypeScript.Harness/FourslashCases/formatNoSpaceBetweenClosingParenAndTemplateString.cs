// ported from: internal/fourslash/tests/formatNoSpaceBetweenClosingParenAndTemplateString_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatNoSpaceBetweenClosingParenAndTemplateStringCase
{
    public static void TestFormatNoSpaceBetweenClosingParenAndTemplateString(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("foo() `abc`;\nbar()`def`;\nbaz()`a${x}b`;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "foo()`abc`;\nbar()`def`;\nbaz()`a${x}b`;");
            }
            finally
            {
                done();
            }
        });
    }

}
