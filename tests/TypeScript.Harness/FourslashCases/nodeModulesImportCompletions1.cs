// ported from: internal/fourslash/tests/nodeModulesImportCompletions1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NodeModulesImportCompletions1Case
{
    public static void TestNodeModulesImportCompletions1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @module: node18\n// @Filename: /src/module.mts\nexport {}\n// @Filename: /src/module.cts\nexport {}\n// @Filename: /src/module.js\nexport {}\n// @Filename: /src/decl.d.mts\nexport {}\n// @Filename: /src/decl.d.cts\nexport {}\n// @Filename: /src/decl.d.ts\nexport {}\n// @Filename: /src/js.mjs\nexport {}\n// @Filename: /src/js.cjs\nexport {}\n// @Filename: /src/js.js\nexport {}\n// @Filename: /main.mts\nimport {} from \"./src//*1*/\";\nimport mod = require(\"./src//*2*/\");\nconst m = import(\"./src//*3*/\");\n// @Filename: /main.cts\nimport {} from \"./src//*4*/\";\nimport mod = require(\"./src//*5*/\");\nconst m = import(\"./src//*6*/\");\n// @Filename: /main.ts\nimport {} from \"./src//*7*/\";\nimport mod = require(\"./src//*8*/\");\nconst m = import(\"./src//*9*/\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "3", "6", "9"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["decl.cjs", "decl.mjs", "decl.js", "js.cjs", "js.js", "js.mjs", "module.cjs", "module.js", "module.mjs"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "4", "5", "7", "8"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["decl.cjs", "decl.mjs", "decl", "js.cjs", "js", "js.mjs", "module.cjs", "module", "module.mjs"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
