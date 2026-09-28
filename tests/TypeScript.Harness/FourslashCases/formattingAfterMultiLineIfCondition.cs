// ported from: internal/fourslash/tests/formattingAfterMultiLineIfCondition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingAfterMultiLineIfConditionCase
{
    public static void TestFormattingAfterMultiLineIfCondition(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" var foo;\n if (foo &&\n     foo) {\n/*comment*/     // This is a comment\n     foo.toString();\n /**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "}");
                f.GoToMarker(t, "comment");
                f.VerifyCurrentLineContent(t, "    // This is a comment");
            }
            finally
            {
                done();
            }
        });
    }

}
