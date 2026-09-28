// ported from: internal/fourslash/tests/autoImportCompletionExportListAugmentation2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCompletionExportListAugmentation2Case
{
    public static void TestAutoImportCompletionExportListAugmentation2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /node_modules/@sapphire/pieces/index.d.ts\ninterface Container {\n  stores: unknown;\n}\n\ndeclare class Piece {\n  get container(): Container;\n}\n\ndeclare class AliasPiece extends Piece {}\n\nexport { AliasPiece, type Container };\n// @Filename: /node_modules/@sapphire/framework/index.d.ts\nimport { AliasPiece } from \"@sapphire/pieces\";\n\ndeclare class Command extends AliasPiece {}\n\ndeclare module \"@sapphire/pieces\" {\n  interface Container {\n    client: unknown;\n  }\n}\n\nexport { Command };\n// @Filename: /index.ts\nimport \"@sapphire/pieces\";\nimport { Command } from \"@sapphire/framework\";\nclass PingCommand extends Command {\n  /*1*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "container", InsertText = "get container(): Container {\n}", FilterText = "container", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "container", Source = "ClassMemberSnippet/", Description = "Includes imports of types referenced by 'container'", NewFileContent = "import \"@sapphire/pieces\";\nimport { Command } from \"@sapphire/framework\";\nimport { Container } from \"@sapphire/pieces\";\nclass PingCommand extends Command {\n  \n}", UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
