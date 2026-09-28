// ported from: internal/fourslash/tests/goToSourceDefinition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionCase
{
    public static void TestGoToSourceNodeModulesWithTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/foo/package.json\n{ \"name\": \"foo\", \"version\": \"1.0.0\", \"main\": \"./lib/main.js\", \"types\": \"./types/main.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/foo/lib/main.js\nexport const /*end*/a = \"a\";\n// @Filename: /home/src/workspaces/project/node_modules/foo/types/main.d.ts\nexport declare const a: string;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { a } from \"foo\";\n[|a/*start*/|]"u8);
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

    public static void TestGoToSourceLocalJsBesideDts(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/a.js\nexport const /*end*/a = \"a\";\n// @Filename: /home/src/workspaces/project/a.d.ts\nexport declare const a: string;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { a } from [|\"./a\"/*moduleSpecifier*/|];\n[|a/*identifier*/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "identifier", "moduleSpecifier");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceNonDeclarationFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/utils.ts\nexport function /*target*/helper() { return 1; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { helper } from \"./utils\";\nhelper/*usage*/();"u8);
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

    public static void TestGoToSourceNoImplementationFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function typesOnly(): void;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/typesOnly } from \"pkg\";\ntypesOnly/*callSite*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importName", "callSite");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceDeclarationMapSourceMap(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./dist/index.js\", \"types\": \"./dist/index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/src/index.ts\nexport function /*target*/greet() { return \"hi\"; }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/dist/index.d.ts\nexport declare function greet(): string;\n//# sourceMappingURL=index.d.ts.map\n// @Filename: /home/src/workspaces/project/node_modules/pkg/dist/index.d.ts.map\n{\"version\":3,\"file\":\"index.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"../src/index.ts\"],\"names\":[],\"mappings\":\"AAAA,wBAAgB,KAAK,WAAY\"}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/dist/index.js\n\"use strict\";\nObject.defineProperty(exports, \"__esModule\", { value: true });\nexports.greet = greet;\nfunction greet() { return \"hi\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { greet } from \"pkg\";\ngreet/*usage*/();"u8);
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

    public static void TestGoToSourceDeclarationMapFallback(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./dist/index.js\", \"types\": \"./dist/index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/src/index.ts\n/*target*/console.log(\"side effect\");\nexport function greet() { return \"hi\"; }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/dist/index.d.ts\nexport declare function greet(): string;\n//# sourceMappingURL=index.d.ts.map\n// @Filename: /home/src/workspaces/project/node_modules/pkg/dist/index.d.ts.map\n{\"version\":3,\"file\":\"index.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"../src/index.ts\"],\"names\":[],\"mappings\":\"AAC6B\"}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/dist/index.js\n\"use strict\";\nObject.defineProperty(exports, \"__esModule\", { value: true });\nexports.greet = greet;\nconsole.log(\"side effect\");\nfunction greet() { return \"hi\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { greet } from \"pkg\";\ngreet/*usage*/();"u8);
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

    public static void TestGoToSourceNamedExportsSpecifier(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function foo(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*target*/foo() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { foo } from \"pkg\";\nconst result = foo/*valueUsage*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "valueUsage");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceTripleSlashReference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /home/src/workspaces/project/helper.js\n/*target*/function helper() { return 1; }\n// @Filename: /home/src/workspaces/project/index.ts\n/// <reference path=\"./[|helper.js/*refPath*/|]\" />\ndeclare function helper(): number;\nhelper();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "refPath");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceFallbackToModuleSpecifier(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @allowJs: true\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function internalHelper(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\n/*entryPoint*/Object.defineProperty(exports, \"internalHelper\", { value: function() {} });\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/internalHelper } from \"pkg\";"u8);
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

    public static void TestGoToSourceFilterPreferredFallbackAll(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @allowJs: true\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./barrel.js\", \"types\": \"./barrel.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/barrel.d.ts\nexport { value } from \"./impl\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/impl.d.ts\nexport declare const value: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/barrel.js\nexport { value } from \"./impl.js\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/impl.js\nexport const /*target*/value = 42;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/value } from \"pkg\";\nconsole.log(value);"u8);
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
