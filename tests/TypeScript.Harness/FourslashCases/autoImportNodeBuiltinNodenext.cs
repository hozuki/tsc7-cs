// ported from: internal/fourslash/tests/autoImportNodeBuiltinNodenext_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportNodeBuiltinNodenextCase
{
    public static void TestAutoImportNodeBuiltinNodenext(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"nodenext\", \"types\": [\"node\"] } }\n// @Filename: /package.json\n{ \"type\": \"module\" }\n// @Filename: /node_modules/@types/node/package.json\n{ \"name\": \"@types/node\", \"version\": \"22.0.0\" }\n// @Filename: /node_modules/@types/node/index.d.ts\ndeclare module \"fs\" {\n    export function existsSync(path: string): boolean;\n    export function mkdirSync(path: string, options?: { recursive?: boolean }): void;\n}\ndeclare module \"node:fs\" { export * from \"fs\"; }\n// @Filename: /index.ts\nexistsSync/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "existsSync", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "node:fs" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
