// ported from: internal/fourslash/tests/jsdocSatisfiesTagFindAllReferences_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocSatisfiesTagFindAllReferencesCase
{
    public static void TestJsdocSatisfiesTagFindAllReferences(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJS: true\n// @checkJs: true\n// @filename: /a.js\n/**\n * @typedef {Object} T\n * @property {number} a\n */\n\n/** @satisfies {/**/T} comment */\nconst foo = { a: 1 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
