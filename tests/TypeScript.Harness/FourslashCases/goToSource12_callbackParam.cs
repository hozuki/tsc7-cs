// ported from: internal/fourslash/tests/goToSource12_callbackParam_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource12_callbackParamCase
{
    public static void TestGoToSource12_callbackParam(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/@types/yargs/package.json\n{\n    \"name\": \"@types/yargs\",\n    \"version\": \"1.0.0\",\n    \"types\": \"./index.d.ts\"\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/yargs/index.d.ts\nexport interface Yargs { positional(): Yargs; }\nexport declare function command(command: string, cb: (yargs: Yargs) => void): void;\n// @Filename: /home/src/workspaces/project/node_modules/yargs/package.json\n{\n    \"name\": \"yargs\",\n    \"version\": \"1.0.0\",\n    \"main\": \"index.js\"\n}\n// @Filename: /home/src/workspaces/project/node_modules/yargs/index.js\nexport function command(cmd, cb) { cb({ /*end*/positional: \"This is obviously not even close to realistic\" }); }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { command } from \"yargs\";\ncommand(\"foo\", yargs => {\n    yargs.[|/*start*/positional|]();\n});"u8);
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
