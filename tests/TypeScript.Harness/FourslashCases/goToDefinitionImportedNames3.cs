// ported from: internal/fourslash/tests/goToDefinitionImportedNames3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionImportedNames3Case
{
    public static void TestGoToDefinitionImportedNames3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: e.ts\n import {M, [|/*classAliasDefinition*/C|], I} from \"./d\";\n var c = new [|/*classReference*/C|]();\n// @Filename: d.ts\nexport * from \"./c\";\n// @Filename: c.ts\nexport {Module as M, Class as C, Interface as I} from \"./b\";\n// @Filename: b.ts\nexport * from \"./a\";\n// @Filename: a.ts\nexport namespace Module {\n}\nexport class /*classDefinition*/Class {\n    private f;\n}\nexport interface Interface {\n    x;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "classReference", "classAliasDefinition");
            }
            finally
            {
                done();
            }
        });
    }

}
