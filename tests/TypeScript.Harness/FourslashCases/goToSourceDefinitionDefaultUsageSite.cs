// ported from: internal/fourslash/tests/goToSourceDefinitionDefaultUsageSite_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionDefaultUsageSiteCase
{
    public static void TestGoToSourceDefaultImportUsageSiteChecker(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport default class Widget {\n    render(): void;\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport default class /*targetWidget*/Widget {\n    /*targetRender*/render() {}\n}\n// @Filename: /home/src/workspaces/project/index.ts\nimport Widget from \"pkg\";\nconst w = new Widget/*constructUsage*/(\"test\");\nw./*methodUsage*/render();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "constructUsage", "methodUsage");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceDefaultImportReExportUsage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport default function greet(name: string): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport default function /*targetGreet*/greet(name) { return \"Hello, \" + name; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport greet from \"pkg\";\ngreet/*callUsage*/(\"world\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "callUsage");
            }
            finally
            {
                done();
            }
        });
    }

}
