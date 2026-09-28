// ported from: internal/fourslash/tests/goToDefinitionPreferSourceDefinition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionPreferSourceDefinitionCase
{
    public static void TestGoToDefinitionPreferSourceDefinition(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/a.js\nexport const /*sourceTarget*/a = \"a\";\n// @Filename: /home/src/workspaces/project/a.d.ts\nexport declare const /*dtsTarget*/a: string;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { a } from \"./a\";\na/*start*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, false, "start");
                f.VerifyBaselineGoToSourceDefinition(t, "start");
                f.Configure(t, new LSUP.UserPreferences { PreferGoToSourceDefinition = true });
                f.VerifyBaselineGoToDefinition(t, false, "start");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToDefinitionPreferSourceDefinitionFallback(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport interface Config {\n    enabled: boolean;\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexports.makeConfig = () => ({ enabled: true });\n// @Filename: /home/src/workspaces/project/index.ts\nimport type { Config } from \"pkg\";\nlet value: /*start*/Config;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { PreferGoToSourceDefinition = true });
                f.VerifyBaselineGoToDefinition(t, false, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
