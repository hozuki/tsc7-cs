// ported from: internal/fourslash/tests/goToSourceDefinitionProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionPropertiesCase
{
    public static void TestGoToSourceAccessExpressionProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare const obj: { greet(name: string): string; count: number; };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const /*targetObj*/obj = { /*targetGreet*/greet(name) { return name; }, /*targetCount*/count: 42 };\n// @Filename: /home/src/workspaces/project/index.ts\nimport { obj } from \"pkg\";\nobj./*propAccess*/greet(\"world\");\nobj./*propAccess2*/count;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "propAccess", "propAccess2");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourcePropertyOfAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/a.js\nexport const a = { /*end*/a: 'a' };\n// @Filename: /home/src/workspaces/project/a.d.ts\nexport declare const a: { a: string };\n// @Filename: /home/src/workspaces/project/b.ts\nimport { a } from './a';\na.[|a/*start*/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "start");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceIndexSignatureProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare const config: { readonly [key: string]: string; name: string };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const config = { /*targetName*/name: \"test\" };\n// @Filename: /home/src/workspaces/project/index.ts\nimport { config } from \"pkg\";\nconfig./*propAccess*/name;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "propAccess");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceMappedTypeProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\ntype Keys = \"a\" | \"b\";\nexport declare const obj: { [K in Keys]: number };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const obj = { a: 1, /*target*/b: 2 };\n// @Filename: /home/src/workspaces/project/index.ts\nimport { obj } from \"pkg\";\nobj./*propAccess*/b;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "propAccess");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceCommonJSAliasPrefersDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare enum TargetPopulation {\n    Team = \"team\",\n    Internal = \"internal\",\n    Insiders = \"insider\",\n    Public = \"public\",\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\n\"use strict\";\nObject.defineProperty(exports, \"__esModule\", { value: true });\nexports.TargetPopulation = void 0;\nvar TargetPopulation;\n(function (TargetPopulation) {\n    TargetPopulation[\"Team\"] = \"team\";\n    TargetPopulation[\"Internal\"] = \"internal\";\n    TargetPopulation[\"Insiders\"] = \"insider\";\n    TargetPopulation[\"Public\"] = \"public\";\n})(TargetPopulation || (exports.TargetPopulation = TargetPopulation = {}));\n// @Filename: /home/src/workspaces/project/index.ts\nimport * as tas from \"pkg\";\ntas./*start*/TargetPopulation.Public;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
