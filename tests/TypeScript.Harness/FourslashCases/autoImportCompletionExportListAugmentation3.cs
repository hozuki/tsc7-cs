// ported from: internal/fourslash/tests/autoImportCompletionExportListAugmentation3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCompletionExportListAugmentation3Case
{
    public static void TestAutoImportCompletionExportListAugmentation3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /node_modules/@sapphire/pieces/index.d.ts\nexport interface Container {\n  stores: unknown;\n}\n\ndeclare class Piece {\n  container: Container;\n}\n\nexport { Piece };\n// @FileName: /augmentation.ts\ndeclare module \"@sapphire/pieces\" {\n  interface Container {\n    client: unknown;\n  }\n}\n// @Filename: /index.ts\nimport { Piece } from \"@sapphire/pieces\";\nclass FullPiece extends Piece {\n  /*1*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "container", InsertText = "container: Container;", FilterText = "container", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "container", Source = "ClassMemberSnippet/", Description = "Includes imports of types referenced by 'container'", NewFileContent = "import { Container, Piece } from \"@sapphire/pieces\";\nclass FullPiece extends Piece {\n  \n}", UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
