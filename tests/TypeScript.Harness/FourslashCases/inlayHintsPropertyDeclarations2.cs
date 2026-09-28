// ported from: internal/fourslash/tests/inlayHintsPropertyDeclarations2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsPropertyDeclarations2Case
{
    public static void TestInlayHintsPropertyDeclarations2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @target: esnext\nclass C {\n    accessor a = 1\n    accessor b: number = 2\n    accessor c;\n    accessor d;\n\n    constructor(value: number) {\n        this.d = value;\n        if (value <= 0) {\n            this.d = null;\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayPropertyDeclarationTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
