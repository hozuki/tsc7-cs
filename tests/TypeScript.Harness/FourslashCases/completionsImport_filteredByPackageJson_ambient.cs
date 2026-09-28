// ported from: internal/fourslash/tests/completionsImport_filteredByPackageJson_ambient_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_filteredByPackageJson_ambientCase
{
    public static void TestCompletionsImport_filteredByPackageJson_ambient(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n//@noEmit: true\n//@Filename: /package.json\n{\n  \"dependencies\": {\n    \"react-syntax-highlighter\": \"*\",\n    \"declared-by-foo\": \"*\"\n  }\n}\n//@Filename: /node_modules/@types/foo/index.d.ts\ndeclare module \"foo\" {\n  export const foo: any;\n}\ndeclare module \"declared-by-foo\" {\n  export const declaredBySomethingNotInPackageJson: any;\n}\n//@Filename: /node_modules/@types/foo/package.json\n{\n  \"name\": \"@types/node\"\n}\n//@Filename: /node_modules/@types/react-syntax-highlighter/index.d.ts\ndeclare module \"react-syntax-highlighter/sub\" {\n  const agate: any;\n  export default agate;\n}\ndeclare module \"something-else\" {\n  export const somethingElse: any;  \n}\n//@Filename: /node_modules/@types/react-syntax-highlighter/package.json\n{\n  \"name\": \"@types/react-syntax-highlighter\"\n}\n//@Filename: /src/ambient.ts\ndeclare module \"local\" {\n  export const local: any';\n}\n//@Filename: /src/index.ts\nfo/*1*/\naga/*2*/\nsomethi/*3*/\ndeclaredBy/*4*/\nloca/*5*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobals } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "agate", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "react-syntax-highlighter/sub" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "somethingElse", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "something-else" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["declaredBySomethingNotInPackageJson"] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "local", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "local" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
