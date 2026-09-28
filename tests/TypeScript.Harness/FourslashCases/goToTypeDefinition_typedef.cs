// ported from: internal/fourslash/tests/goToTypeDefinition_typedef_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinition_typedefCase
{
    public static void TestGoToTypeDefinition_typedef(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @allowJs: true\n// @Filename: /a.js\n/**\n * /*def*/@typedef {object} I\n * @property {number} x\n */\n\n/** @type {I} */\nconst /*ref*/i = { x: 0 };";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "ref");
            }
            finally
            {
                done();
            }
        });
    }

}
