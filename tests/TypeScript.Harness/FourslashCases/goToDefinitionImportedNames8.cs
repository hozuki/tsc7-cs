// ported from: internal/fourslash/tests/goToDefinitionImportedNames8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionImportedNames8Case
{
    public static void TestGoToDefinitionImportedNames8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowjs: true\n// @Filename: b.js\nimport { [|/*classAliasDefinition*/Class|] } from \"./a\";\n// @Filename: a.js\nclass /*classDefinition*/Class {\n    private f;\n}\n export { Class };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "classAliasDefinition");
            }
            finally
            {
                done();
            }
        });
    }

}
