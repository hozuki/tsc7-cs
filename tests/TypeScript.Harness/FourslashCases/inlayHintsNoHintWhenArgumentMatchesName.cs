// ported from: internal/fourslash/tests/inlayHintsNoHintWhenArgumentMatchesName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsNoHintWhenArgumentMatchesNameCase
{
    public static void TestInlayHintsNoHintWhenArgumentMatchesName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo (a: number, b: number) {}\ndeclare const a: 1;\nfoo(a, 2);\ndeclare const v: any;\nfoo(v.a, v.a);\nfoo(v.b, v.b);\nfoo(v.c, v.c);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsAll, IncludeInlayParameterNameHintsWhenArgumentMatchesName = TRI.TSFalse } });
            }
            finally
            {
                done();
            }
        });
    }

}
