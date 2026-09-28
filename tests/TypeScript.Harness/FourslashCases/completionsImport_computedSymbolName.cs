// ported from: internal/fourslash/tests/completionsImport_computedSymbolName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_computedSymbolNameCase
{
    public static void TestCompletionsImport_computedSymbolName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/node_modules/@types/ts-node/index.d.ts\nexport {};\ndeclare const REGISTER_INSTANCE: unique symbol;\ndeclare global {\n    namespace NodeJS {\n      interface Process {\n          [REGISTER_INSTANCE]?: Service;\n      }\n  }\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/node/index.d.ts\ndeclare module \"process\" {\n    global {\n        var process: NodeJS.Process;\n        namespace NodeJS {\n            interface Process {\n                argv: string[];\n            }\n        }\n    }\n    export = process;\n}\n// @Filename: /home/src/workspaces/project/index.ts\nI/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
                f.Insert(t, "N");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done();
            }
        });
    }

}
