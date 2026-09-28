// ported from: internal/fourslash/tests/inlayHintsInteractiveParameterNamesWithComments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveParameterNamesWithCommentsCase
{
    public static void TestInlayHintsInteractiveParameterNamesWithComments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const fn = (x: any) => { }\nfn(/* nobody knows exactly what this param is */ 42);\nfunction foo (aParameter: number, bParameter: number, cParameter: number) { }\nfoo(\n    /** aParameter */\n    1,\n    // bParameter\n    2,\n    /* cParameter */\n    3\n)\nfoo(\n    /** multiple comments */\n    /** aParameter */\n    1,\n    /** bParameter */\n    /** multiple comments */\n    2,\n    // cParameter\n    /** multiple comments */\n    3\n)\nfoo(\n    /** wrong name */\n    1,\n    2,\n    /** multiple */\n    /** wrong */\n    /** name */\n    3\n)"u8);
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
