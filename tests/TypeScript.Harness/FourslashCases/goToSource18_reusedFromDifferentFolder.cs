// ported from: internal/fourslash/tests/goToSource18_reusedFromDifferentFolder_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource18_reusedFromDifferentFolderCase
{
    public static void TestGoToSource18_reusedFromDifferentFolder(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/@types/yargs/package.json\n{\n    \"name\": \"@types/yargs\",\n    \"version\": \"1.0.0\",\n    \"types\": \"./index.d.ts\"\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/yargs/callback.d.ts\nexport declare class Yargs { positional(): Yargs; }\n// @Filename: /home/src/workspaces/project/node_modules/@types/yargs/index.d.ts\nimport { Yargs } from \"./callback\";\nexport declare function command(command: string, cb: (yargs: Yargs) => void): void;\n// @Filename: /home/src/workspaces/project/node_modules/yargs/package.json\n{\n    \"name\": \"yargs\",\n    \"version\": \"1.0.0\",\n    \"main\": \"index.js\"\n}\n// @Filename: /home/src/workspaces/project/node_modules/yargs/callback.js\nexport class Yargs { positional() { } }\n// @Filename: /home/src/workspaces/project/node_modules/yargs/index.js\n// Specifically didnt have ./callback import to ensure that resolving module sepcifier adds the file to project at later stage\nexport function command(cmd, cb) { cb(Yargs) }\n// @Filename: /home/src/workspaces/project/folder/random.ts\nimport { Yargs } from \"yargs/callback\";\n// @Filename: /home/src/workspaces/project/some/index.ts\nimport { random } from \"../folder/random\";\nimport { command } from \"yargs\";\ncommand(\"foo\", yargs => {\n    yargs.[|/*start*/positional|]();\n});"u8);
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
