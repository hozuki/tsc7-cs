// ported from: internal/fourslash/tests/autoImportCompletionExportEqualsWithDefault1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCompletionExportEqualsWithDefault1Case
{
    public static void TestAutoImportCompletionExportEqualsWithDefault1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @module: commonjs\n// @esModuleInterop: false\n// @allowSyntheticDefaultImports: false\n// @filename: node.ts\nimport Container from \"./container.js\";\nimport Document from \"./document.js\";\n\ndeclare namespace Node {\n  class Node extends Node_ {}\n\n  export { Node as default };\n}\n\ndeclare abstract class Node_ {\n  parent: Container | Document | undefined;\n}\n\ndeclare class Node extends Node_ {}\n\nexport = Node;\n// @filename: document.ts\nimport Container from \"./container.js\";\n\ndeclare namespace Document {\n  export { Document_ as default };\n}\n\ndeclare class Document_ extends Container {}\n\ndeclare class Document extends Document_ {}\n\nexport = Document;\n// @filename: container.ts\nimport Node from \"./node.js\";\n\ndeclare namespace Container {\n  export { Container_ as default };\n}\n\ndeclare abstract class Container_ extends Node {\n  p/*1*/\n}\n\ndeclare class Container extends Container_ {}\n\nexport = Container;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "parent", InsertText = "parent: Container_ | Document_ | undefined;", FilterText = "parent", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "parent", Source = "ClassMemberSnippet/", Description = "Includes imports of types referenced by 'parent'", NewFileContent = "import Document_ from \"./document.js\";\nimport Node from \"./node.js\";\n\ndeclare namespace Container {\n  export { Container_ as default };\n}\n\ndeclare abstract class Container_ extends Node {\n  p\n}\n\ndeclare class Container extends Container_ {}\n\nexport = Container;", UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
