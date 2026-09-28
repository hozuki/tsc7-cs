// ported from: internal/fourslash/tests/smartSelection_JSDocTags4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SmartSelection_JSDocTags4Case
{
    public static void TestSmartSelection_JSDocTags4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "/**\n * @typedef {object} Foo\n * @property {string} a\n * @property {number} b\n * @property {/**/number} c\n */\n\n/** @type {Foo} */\nconst foo;";
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
