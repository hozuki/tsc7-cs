// ported from: internal/fourslash/tests/regexErrorRecovery_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RegexErrorRecoveryCase
{
    public static void TestRegexErrorRecovery(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" // test code\n//var x = //**/a/;/*1*/\n//x.exec(\"bab\");\n Bug 579071: Parser no longer detects a Regex when an open bracket is inserted\nverify.quickInfoIs(\"RegExp\");\nverify.not.errorExistsAfterMarker(\"1\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Insert(t, "(");
            }
            finally
            {
                done();
            }
        });
    }

}
