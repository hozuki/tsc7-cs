// ported from: internal/fourslash/tests/goToDefinitionJsModuleExports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionJsModuleExportsCase
{
    public static void TestGoToDefinitionJsModuleExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: foo.js\nx./*def*/test = () => { }\nx.[|/*ref*/test|]();\nx./*defFn*/test3 = function () { }\nx.[|/*refFn*/test3|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "ref", "refFn");
            }
            finally
            {
                done();
            }
        });
    }

}
