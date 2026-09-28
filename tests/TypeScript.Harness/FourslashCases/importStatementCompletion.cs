// ported from: internal/fourslash/tests/importStatementCompletion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSU = TypeScript.LsUtil.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportStatementCompletionCase
{
    public static void TestImportStatementCompletionUsesNamedImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport interface I {}\n// @Filename: 1.ts\nimport * as u from \"./a\";\n[|import I/*a*/|]"u8);
            var preferences = LSU.NewDefaultUserPreferences();
            preferences!.IncludeCompletionsForModuleExports = TRI.TSFalse;
            preferences!.IncludeCompletionsForImportStatements = TRI.TSTrue;
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "I", InsertText = "import { I } from \"./a\";", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./a" } }, TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "import { I } from \"./a\";", Range = f.Ranges()[0].LSRange } } }] }, UserPreferences = preferences });
            }
            finally
            {
                done();
            }
        });
    }

}
