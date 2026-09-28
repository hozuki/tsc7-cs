// ported from: internal/fourslash/tests/goToSourceDefinitionNestedNodeModules_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionNestedNodeModulesCase
{
    public static void TestGoToSourceNestedNodeModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/outer/package.json\n{ \"name\": \"outer\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/outer/index.d.ts\nexport { inner } from \"./node_modules/inner/index\";\n// @Filename: /home/src/workspaces/project/node_modules/outer/index.js\nexport { inner } from \"./node_modules/inner/index.js\";\n// @Filename: /home/src/workspaces/project/node_modules/outer/node_modules/inner/package.json\n{ \"name\": \"inner\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/outer/node_modules/inner/index.d.ts\nexport declare function inner(): string;\n// @Filename: /home/src/workspaces/project/node_modules/outer/node_modules/inner/index.js\nexport function /*target*/inner() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/inner } from \"outer\";\ninner/*usage*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importName", "usage");
            }
            finally
            {
                done();
            }
        });
    }

}
