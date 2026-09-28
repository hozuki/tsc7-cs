// ported from: internal/fourslash/tests/findAllReferencesJsDocTypeLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesJsDocTypeLiteralCase
{
    public static void TestFindAllReferencesJsDocTypeLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: foo.js\n/**\n * @param {object} o - very important!\n * @param {string} o.x - a thing, its ok\n * @param {number} o.y - another thing\n * @param {Object} o.nested - very nested\n * @param {boolean} o.nested./*1*/great - much greatness\n * @param {number} o.nested.times - twice? probably!??\n */\n function f(o) { return o.nested./*2*/great; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
