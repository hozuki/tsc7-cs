// ported from: internal/fourslash/tests/goToSourceDefinitionImportVariants_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionImportVariantsCase
{
    public static void TestGoToSourceRequireCall(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @allowJs: true\n// @checkJs: true\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function helper(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexports./*target*/helper = function() { return \"ok\"; };\n// @Filename: /home/src/workspaces/project/index.js\nconst { /*importName*/helper } = require(\"pkg\");\nhelper/*usage*/();"u8);
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

    public static void TestGoToSourceDynamicImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @target: esnext\n// @module: esnext\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function dynHelper(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*target*/dynHelper() { return \"dynamic\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nasync function main() {\n    const mod = await import(\"pkg\");\n    mod./*usage*/dynHelper();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "usage");
            }
            finally
            {
                done();
            }
        });
    }

}
