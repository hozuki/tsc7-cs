// ported from: internal/fourslash/tests/renameStringLiteralTypes1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameStringLiteralTypes1Case
{
    public static void TestRenameStringLiteralTypes1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface AnimationOptions {\n    deltaX: number;\n    deltaY: number;\n    easing: \"ease-in\" | \"ease-out\" | \"[|ease-in-out|]\";\n}\n\nfunction animate(o: AnimationOptions) { }\n\nanimate({ deltaX: 100, deltaY: 100, easing: \"[|ease-in-out|]\" });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "ease-in-out");
            }
            finally
            {
                done();
            }
        });
    }

}
