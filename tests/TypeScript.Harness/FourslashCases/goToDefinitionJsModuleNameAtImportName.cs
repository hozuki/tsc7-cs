// ported from: internal/fourslash/tests/goToDefinitionJsModuleNameAtImportName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionJsModuleNameAtImportNameCase
{
    public static void TestGoToDefinitionJsModuleNameAtImportName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /foo.js\n /*moduleDef*/function notExported() { }\n class Blah {\n    abc = 123;\n }\n module.exports.Blah = Blah;\n// @Filename: /bar.js\nconst [|/*importDef*/BlahModule|] = require(\"./foo.js\");\nnew [|/*importUsage*/BlahModule|].Blah()\n// @Filename: /barTs.ts\nimport [|/*importDefTs*/BlahModule|] = require(\"./foo.js\");\nnew [|/*importUsageTs*/BlahModule|].Blah()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "importDef", "importUsage", "importDefTs", "importUsageTs");
            }
            finally
            {
                done();
            }
        });
    }

}
