// ported from: internal/fourslash/tests/formatBracketInSwitchCase_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatBracketInSwitchCaseCase
{
    public static void TestFormatBracketInSwitchCase(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nswitch (x) {\n    case[]:\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "switch (x) {\n    case []:\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
