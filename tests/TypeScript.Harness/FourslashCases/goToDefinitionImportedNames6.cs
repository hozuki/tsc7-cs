// ported from: internal/fourslash/tests/goToDefinitionImportedNames6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionImportedNames6Case
{
    public static void TestGoToDefinitionImportedNames6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: b.ts\nimport [|/*moduleAliasDefinition*/alias|] = require(\"./a\");\n// @Filename: a.ts\n/*moduleDefinition*/export namespace Module {\n}\nexport class Class {\n    private f;\n}\nexport interface Interface {\n    x;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "moduleAliasDefinition");
            }
            finally
            {
                done();
            }
        });
    }

}
