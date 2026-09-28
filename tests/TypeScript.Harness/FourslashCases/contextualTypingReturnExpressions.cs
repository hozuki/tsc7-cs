// ported from: internal/fourslash/tests/contextualTypingReturnExpressions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ContextualTypingReturnExpressionsCase
{
    public static void TestContextualTypingReturnExpressions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A { }\nvar f44: (x: A) => (y: A) => A = /*1*/x => /*2*/y => /*3*/x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) x: A", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) y: A", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) x: A", "");
            }
            finally
            {
                done();
            }
        });
    }

}
