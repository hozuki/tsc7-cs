// ported from: internal/fourslash/tests/inlayHintsInteractiveAnyParameter2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveAnyParameter2Case
{
    public static void TestInlayHintsInteractiveAnyParameter2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo (v: any) {}\nfoo(1);\nfoo('');\nfoo(true);\nfoo(() => 1);\nfoo(function () { return 1 });\nfoo({});\nfoo({ a: 1 });\nfoo([]);\nfoo([1]);\nfoo(foo);\nfoo((1));\nfoo(foo(1));"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsAll } });
            }
            finally
            {
                done();
            }
        });
    }

}
