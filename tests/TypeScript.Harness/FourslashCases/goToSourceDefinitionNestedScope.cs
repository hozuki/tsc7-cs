// ported from: internal/fourslash/tests/goToSourceDefinitionNestedScope_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionNestedScopeCase
{
    public static void TestGoToSourceNestedScopeShadowing(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function helper(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*targetHelper*/helper() { return \"ok\"; }\nfunction unrelated() {\n    const helper = \"shadow\";\n    return helper;\n}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importHelper*/helper } from \"pkg\";\nhelper/*usage*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importHelper", "usage");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceNestedClassShadowing(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare class Widget {}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport class /*targetWidget*/Widget {}\nfunction factory() {\n    class Widget { constructor() { this.local = true; } }\n    return new Widget();\n}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importWidget*/Widget } from \"pkg\";\nnew Widget();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importWidget");
            }
            finally
            {
                done();
            }
        });
    }

}
