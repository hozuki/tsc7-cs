// ported from: internal/fourslash/tests/goToSourceDefinitionTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionTypesCase
{
    public static void TestGoToSourceFallbacksToDefinitionForInterface(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport interface /*target*/Config {\n    enabled: boolean;\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexports.makeConfig = () => ({ enabled: true });\n// @Filename: /home/src/workspaces/project/index.ts\nimport type { /*importName*/Config } from \"pkg\";\nlet value: /*typeRef*/Config;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importName", "typeRef");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceTypeOnlySymbolFallback(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/types.d.ts\nexport interface Config { enabled: boolean; }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/types.js\n// no runtime content for Config interface\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport { Config } from \"./types\";\nexport declare function makeConfig(): Config;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport { Config } from \"./types.js\";\nexport function makeConfig() { return { enabled: true }; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { Config, makeConfig } from \"pkg\";\nlet c: /*typeRef*/Config;\nmakeConfig/*callRef*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "typeRef", "callRef");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceForwardedNonConcreteMerge(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @allowJs: true\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport { Config } from \"./types\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/types.d.ts\nexport interface Config { enabled: boolean; }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport { Config } from \"./types.js\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/types.js\n// Config is a type, no runtime value\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/Config } from \"pkg\";\nlet c: Config;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importName");
            }
            finally
            {
                done();
            }
        });
    }

}
