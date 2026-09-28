// ported from: internal/fourslash/tests/smartSelection_JSDocTags6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SmartSelection_JSDocTags6Case
{
    public static void TestSmartSelection_JSDocTags6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "/**\n * @template T\n * @param {/**/T} x\n * @return {T}\n */\nfunction foo(x) {\n    return x;\n}";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineSelectionRanges(t);
            }
            finally
            {
                done();
            }
        });
    }

}
