// ported from: internal/fourslash/tests/quickInfoFunctionKeyword_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoFunctionKeywordCase
{
    public static void TestQuickInfoFunctionKeyword(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[1].forEach(fu/*1*/nction() {});\n[1].map(x =/*2*/> x + 1);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(local function)(): void", "");
                f.VerifyQuickInfoAt(t, "2", "function(x: number): number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
