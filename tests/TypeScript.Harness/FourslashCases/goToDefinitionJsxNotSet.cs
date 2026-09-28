// ported from: internal/fourslash/tests/goToDefinitionJsxNotSet_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionJsxNotSetCase
{
    public static void TestGoToDefinitionJsxNotSet(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /foo.jsx\nconst /*def*/Foo = () => (\n    <div>foo</div>\n);\nexport default Foo;\n// @Filename: /bar.jsx\nimport Foo from './foo';\nconst a = <[|/*use*/Foo|] />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "use");
            }
            finally
            {
                done();
            }
        });
    }

}
