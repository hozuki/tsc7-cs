// ported from: internal/fourslash/tests/findAllRefsJSDocNamespacedTypedef_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsJSDocNamespacedTypedefCase
{
    public static void TestFindAllRefsJSDocNamespacedTypedef(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @allowJs: true\n// @checkJs: true\n// @Filename: /index.js\n// Namespaced typedef\n/** @typedef {string} [|NS|].[|T|] */\n\n// Namespaced typedef aliased to qualified namespaced typedef.\n/** @typedef {NS.T} NS.[|U|] */\n\n// Namespaced typedef aliased to implicitly-resolved typedef.\n/** @typedef {U} NS.[|V|] */\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t);
            }
            finally
            {
                done();
            }
        });
    }

}
