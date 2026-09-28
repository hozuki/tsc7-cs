// ported from: internal/fourslash/tests/completionsImport_duplicatePackages_typesAndNotTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_duplicatePackages_typesAndNotTypesCase
{
    public static void TestCompletionsImport_duplicatePackages_typesAndNotTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: commonjs\n// @esModuleInterop: true\n// @Filename: /node_modules/@types/react-dom/package.json\n{ \"name\": \"react-dom\", \"version\": \"1.0.0\", \"types\": \"./index.d.ts\" }\n// @Filename: /node_modules/@types/react-dom/index.d.ts\nimport * as React from \"react\";\nexport function render(): void;\n// @Filename: /node_modules/@types/react/package.json\n{ \"name\": \"react\", \"version\": \"1.0.0\", \"types\": \"./index.d.ts\" }\n// @Filename: /node_modules/@types/react/index.d.ts\nimport \"./other\";\nexport declare function useState(): void;\n// @Filename: /node_modules/@types/react/other.d.ts\nexport declare function useRef(): void;\n// @Filename: /packages/a/node_modules/react/package.json\n{ \"name\": \"react\", \"version\": \"1.0.1\", \"types\": \"./index.d.ts\" }\n// @Filename: /packages/a/node_modules/react/index.d.ts\nexport declare function useState(): void;\n// @Filename: /packages/a/index.ts\nimport \"react-dom\";\nimport \"react\";\n// @Filename: /packages/a/foo.ts\nuseState/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus([new LSP.CompletionItem { Label = "useState", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "react" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
