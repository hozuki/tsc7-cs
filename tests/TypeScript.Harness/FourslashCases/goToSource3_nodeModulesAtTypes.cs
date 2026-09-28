// ported from: internal/fourslash/tests/goToSource3_nodeModulesAtTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource3_nodeModulesAtTypesCase
{
    public static void TestGoToSource3_nodeModulesAtTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/foo/package.json\n{ \"name\": \"foo\", \"version\": \"1.0.0\", \"main\": \"./lib/main.js\" }\n// @Filename: /home/src/workspaces/project/node_modules/foo/lib/main.js\nexport const /*end*/a = \"a\";\n// @Filename: /home/src/workspaces/project/node_modules/@types/foo/package.json\n{ \"name\": \"@types/foo\", \"version\": \"1.0.0\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/@types/foo/index.d.ts\nexport declare const a: string;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { a } from \"foo\";\n[|a/*start*/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
