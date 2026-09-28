// ported from: internal/fourslash/tests/completionsImport_filteredByPackageJson_@typesImplicit_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_filteredByPackageJson__typesImplicitCase
{
    public static void TestCompletionsImport_filteredByPackageJson_typesImplicit(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@noEmit: true\n//@Filename: /package.json\n{\n  \"dependencies\": {\n    \"react\": \"*\"\n  }\n}\n//@Filename: /node_modules/@types/react/index.d.ts\nexport declare var React: any;\n//@Filename: /node_modules/@types/react/package.json\n{\n  \"name\": \"@types/react\"\n}\n//@Filename: /node_modules/@types/fake-react/index.d.ts\nexport declare var ReactFake: any;\n//@Filename: /node_modules/@types/fake-react/package.json\n{\n  \"name\": \"@types/fake-react\"\n}\n//@Filename: /src/index.ts\nconst x = Re/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "React", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "react" } }, SortText = LS.SortTextAutoImportSuggestions }], Excludes = ["ReactFake"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
