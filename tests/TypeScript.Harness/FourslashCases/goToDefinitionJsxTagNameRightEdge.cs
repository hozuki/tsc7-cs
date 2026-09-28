// ported from: internal/fourslash/tests/goToDefinitionJsxTagNameRightEdge_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionJsxTagNameRightEdgeCase
{
    public static void TestGoToDefinitionJsxTagNameRightEdge(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @jsx: react-jsx\n// @filename: /a.jsx\nexport function Component() {\n    return null;\n}\n\nexport function App() {\n    return <Component/*use*/ />\n}"u8);
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
