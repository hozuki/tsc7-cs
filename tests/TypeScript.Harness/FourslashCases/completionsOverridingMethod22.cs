// ported from: internal/fourslash/tests/completionsOverridingMethod22_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod22Case
{
    public static void TestCompletionsOverridingMethod22(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: b.ts\nexport type C = { x: number }\nexport interface ExtShape {\n    $returnPromise(id: number): Promise<C>;\n    $return(id: number): C;\n}\n// @Filename: test.ts\nimport { ExtShape } from './b';\nabstract class ExtBase implements ExtShape {\n    public [|$/**/|]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "$returnPromise", TextEdit = U.InsertReplaceTextEdit("public \\$returnPromise(id: number): Promise<C> {\n}", f.Ranges()[0].LSRange), FilterText = "$returnPromise", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue }, Name = "$returnPromise", Source = "ClassMemberSnippet/", Description = "Includes imports of types referenced by '$returnPromise'", NewFileContent = "import { C, ExtShape } from './b';\nabstract class ExtBase implements ExtShape {\n    $\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
