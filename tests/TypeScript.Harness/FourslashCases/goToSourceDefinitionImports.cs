// ported from: internal/fourslash/tests/goToSourceDefinitionImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionImportsCase
{
    public static void TestGoToSourceAliasedImportExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare const foo: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexports./*target*/foo = 1;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { foo as /*importAlias*/bar } from \"pkg\";\nbar;\n// @Filename: /home/src/workspaces/project/reexport.ts\nexport { foo as /*reExportAlias*/bar } from \"pkg\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importAlias", "reExportAlias");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceAliasedImportSpecifier(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function original(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*target*/original() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { original as /*aliasedImport*/renamed } from \"pkg\";\nrenamed();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "aliasedImport");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceCallThroughImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare class Widget {\n    constructor(name: string);\n    render(): void;\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport class /*targetWidget*/Widget {\n    constructor(name) { this.name = name; }\n    /*targetRender*/render() {}\n}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { Widget } from \"pkg\";\nconst w = new /*constructorCall*/Widget(\"test\");\nw./*methodCall*/render();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "constructorCall", "methodCall");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceCallbackParam(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/@types/yargs/package.json\n{\n    \"name\": \"@types/yargs\",\n    \"version\": \"1.0.0\",\n    \"types\": \"./index.d.ts\"\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/yargs/index.d.ts\nexport interface Yargs { positional(): Yargs; }\nexport declare function command(command: string, cb: (yargs: Yargs) => void): void;\n// @Filename: /home/src/workspaces/project/node_modules/yargs/package.json\n{\n    \"name\": \"yargs\",\n    \"version\": \"1.0.0\",\n    \"main\": \"index.js\"\n}\n// @Filename: /home/src/workspaces/project/node_modules/yargs/index.js\nexport function command(cmd, cb) { cb({ /*end*/positional: \"This is obviously not even close to realistic\" }); }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { command } from \"yargs\";\ncommand(\"foo\", yargs => {\n    yargs.[|/*start*/positional|]();\n});"u8);
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

    public static void TestGoToSourceReExportNames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function foo(): string;\nexport declare function bar(): number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*targetFoo*/foo() { return \"ok\"; }\nexport function /*targetBar*/bar() { return 42; }\n// @Filename: /home/src/workspaces/project/reexport.ts\nexport { /*reExportFoo*/foo, /*reExportBar*/bar } from \"pkg\";\n// @Filename: /home/src/workspaces/project/index.ts\nimport { foo, bar } from [|\"pkg\"/*moduleSpecifier*/|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "reExportFoo", "reExportBar", "moduleSpecifier");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceReExportModuleSpecifier(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function alpha(): string;\nexport declare function beta(): number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*targetAlpha*/alpha() { return \"a\"; }\nexport function /*targetBeta*/beta() { return 2; }\n// @Filename: /home/src/workspaces/project/reexport.ts\nexport { alpha, beta } from [|\"pkg\"/*reExportSpecifier*/|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "reExportSpecifier");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceReExportedImplementation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\", \"type\": \"module\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport { foo } from \"./foo\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport { foo } from \"./foo.js\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/foo.d.ts\nexport declare function foo(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/foo.js\nexport function /*target*/foo() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/foo } from \"pkg\";\nfoo/*start*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importName", "start");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceImportFilteredByExternalDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function helper(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*target*/helper() {}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { helper } from \"pkg\";\nhelper/*usage*/();\nexport { helper as /*reExport*/myHelper } from \"pkg\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "usage", "reExport");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceDtsReExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/impl.d.ts\nexport declare function helper(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/impl.js\nexport function /*target*/helper() {}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport { helper } from \"./impl\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport { helper } from \"./impl.js\";\n// @Filename: /home/src/workspaces/project/index.ts\nimport { helper } from \"pkg\";\nhelper/*usage*/();"u8);
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

    public static void TestGoToSourceBarrelReExportChain(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/impl.js\nexport function /*target*/doWork() { return 42; }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/impl.d.ts\nexport declare function doWork(): number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport { doWork } from \"./impl\";\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport { doWork } from \"./impl.js\";\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/doWork } from \"pkg\";\ndoWork/*callSite*/();"u8);
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

    public static void TestGoToSourceCJSReExportViaDefineProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function greet(name: string): string;\nexport declare enum TargetPopulation {\n    Team = \"team\",\n    Public = \"public\",\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\n\"use strict\";\nObject.defineProperty(exports, \"__esModule\", { value: true });\nexports.TargetPopulation = exports.greet = void 0;\nvar impl_1 = require(\"./impl\");\nObject.defineProperty(exports, \"greet\", { enumerable: true, get: function () { return impl_1.greet; } });\nvar types_1 = require(\"./types\");\nObject.defineProperty(exports, \"TargetPopulation\", { enumerable: true, get: function () { return types_1.TargetPopulation; } });\n// @Filename: /home/src/workspaces/project/node_modules/pkg/impl.js\n\"use strict\";\nObject.defineProperty(exports, \"__esModule\", { value: true });\nexports.greet = void 0;\nfunction /*greetImpl*/greet(name) { return \"Hello, \" + name; }\nexports.greet = greet;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/types.js\n\"use strict\";\nObject.defineProperty(exports, \"__esModule\", { value: true });\nexports.TargetPopulation = void 0;\nvar /*targetPopulationImpl*/TargetPopulation;\n(function (TargetPopulation) {\n    TargetPopulation[\"Team\"] = \"team\";\n    TargetPopulation[\"Public\"] = \"public\";\n})(TargetPopulation || (exports.TargetPopulation = TargetPopulation = {}));\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*namedImport*/greet, /*enumImport*/TargetPopulation } from \"pkg\";\ngreet/*call*/(\"world\");\nTargetPopulation/*enumAccess*/.Team;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "namedImport", "enumImport", "call", "enumAccess");
            }
            finally
            {
                done();
            }
        });
    }

}
