// ported from: internal/fourslash/tests/inlayHintsInteractiveAnyParameter1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveAnyParameter1Case
{
    public static void TestInlayHintsInteractiveAnyParameter1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo (v: any) {}\nfoo(1);\nfoo('');\nfoo(true);\nfoo(foo);\nfoo((1));\nfoo(foo(1));"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsLiterals } });
            }
            finally
            {
                done();
            }
        });
    }

}
