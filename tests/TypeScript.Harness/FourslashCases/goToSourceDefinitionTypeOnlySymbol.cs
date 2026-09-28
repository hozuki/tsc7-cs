// ported from: internal/fourslash/tests/goToSourceDefinitionTypeOnlySymbol_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionTypeOnlySymbolCase
{
    public static void TestGoToSourceDefinitionTypeOnlyImportFallsBackToDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport interface /*targetDecl*/Config {\n    name: string;\n    value: number;\n}\nexport declare function create(config: Config): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function create(config) { return config; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importConfig*/Config, create } from \"pkg\";\nconst c: Config = { name: \"test\", value: 1 };\ncreate(c);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importConfig");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceDefinitionTypeOnlyUsageFallsBackToDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport interface /*targetDecl*/Config {\n    name: string;\n}\nexport declare function create(config: Config): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function create(config) { return config; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { Config, create } from \"pkg\";\nconst c: /*usageSite*/Config = { name: \"test\" };\ncreate(c);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "usageSite");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceDefinitionValueImportStillWorks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function /*dtsCreate*/create(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*targetCreate*/create() {}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importCreate*/create } from \"pkg\";\ncreate();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importCreate");
            }
            finally
            {
                done();
            }
        });
    }

}
