// ported from: internal/fourslash/tests/autoImportCompletionAmbientMergedModule1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCompletionAmbientMergedModule1Case
{
    public static void TestAutoImportCompletionAmbientMergedModule1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @module: commonjs\n// @filename: /node_modules/@types/vscode/index.d.ts\ndeclare module \"vscode\" {\n  export class Position {\n    readonly line: number;\n    readonly character: number;\n  }\n}\n// @filename: src/motion.ts\nimport { Position } from \"vscode\";\n\nexport abstract class MoveQuoteMatch {\n  public override async execActionWithCount(\n    position: Position,\n  ): Promise<void> {}\n}\n\ndeclare module \"vscode\" {\n  interface Position {\n    toString(): string;\n  }\n}\n// @filename: src/smartQuotes.ts\nimport { MoveQuoteMatch } from \"./motion\";\n\nexport class MoveInsideNextQuote extends MoveQuoteMatch {/*1*/\n  keys = [\"i\", \"n\", \"q\"];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "execActionWithCount", InsertText = "public execActionWithCount(position: Position): Promise<void> {\n}", FilterText = "execActionWithCount", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "execActionWithCount", Source = "ClassMemberSnippet/", Description = "Includes imports of types referenced by 'execActionWithCount'", NewFileContent = "import { Position } from \"vscode\";\nimport { MoveQuoteMatch } from \"./motion\";\n\nexport class MoveInsideNextQuote extends MoveQuoteMatch {\n  keys = [\"i\", \"n\", \"q\"];\n}", UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
