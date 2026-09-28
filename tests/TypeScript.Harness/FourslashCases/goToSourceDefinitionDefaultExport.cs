// ported from: internal/fourslash/tests/goToSourceDefinitionDefaultExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionDefaultExportCase
{
    public static void TestGoToSourceNamedAndDefaultExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport default class Widget {}\nexport declare function helper(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport default class /*targetWidget*/Widget {}\nexport function /*targetHelper*/helper() {}\n// @Filename: /home/src/workspaces/project/index.ts\nimport /*importDefault*/Widget, { /*importHelper*/helper } from \"pkg\";\nWidget;\nhelper();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importDefault", "importHelper");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceDefaultImportNotFirstStatement(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare const version: string;\nexport default class Widget {}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const version = \"1.0\";\nexport default class /*targetWidget*/Widget {}\n// @Filename: /home/src/workspaces/project/index.ts\nimport /*importDefault*/Widget from \"pkg\";\nWidget;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importDefault");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceUnnamedDefaultExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport default function(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport default /*targetDefault*/function() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport /*importDefault*/myFunc from \"pkg\";\nmyFunc/*usage*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importDefault", "usage");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceEmptyNamesEntryFallback(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\ndeclare const _default: { run(): void };\nexport default _default;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport default { run() {} };\n// @Filename: /home/src/workspaces/project/index.ts\nimport /*defaultImport*/pkg from \"pkg\";\npkg.run();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "defaultImport");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceExportAssignmentDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\ndeclare const _default: { run(): void };\nexport default _default;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\n/*target*/export default { run() {} };\n// @Filename: /home/src/workspaces/project/index.ts\nimport pkg from \"pkg\";\npkg/*usage*/;"u8);
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

    public static void TestGoToSourceExportAssignment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/legacy/package.json\n{ \"name\": \"legacy\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/legacy/index.d.ts\ndeclare function legacyFn(): string;\nexport = legacyFn;\n// @Filename: /home/src/workspaces/project/node_modules/legacy/index.js\nfunction /*targetFn*/legacyFn() { return \"ok\"; }\nmodule.exports = legacyFn;\n// @Filename: /home/src/workspaces/project/index.ts\nimport /*importName*/legacyFn from \"legacy\";\nlegacyFn();"u8);
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

    public static void TestGoToSourceExportAssignmentExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport default function createThing(): { value: number };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport default function createThing() { return { value: 42 }; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport /*defaultName*/createThing from \"pkg\";\ncreateThing/*callDefault*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "defaultName", "callDefault");
            }
            finally
            {
                done();
            }
        });
    }

}
