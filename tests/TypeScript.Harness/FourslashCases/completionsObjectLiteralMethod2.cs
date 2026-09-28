// ported from: internal/fourslash/tests/completionsObjectLiteralMethod2_test.go
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

internal static class CompletionsObjectLiteralMethod2Case
{
    public static void TestCompletionsObjectLiteralMethod2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: a.ts\nexport interface IFoo {\n    bar(x: number): void;\n}\n// @Filename: b.ts\nimport { IFoo } from \"./a\";\nexport interface IBar {\n    foo(f: IFoo): void;\n}\n// @Filename: c.ts\nimport { IBar } from \"./b\";\nconst obj: IBar = {\n    /*a*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { LabelDetailsSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", SortText = LS.ObjectLiteralPropertySortText(LS.SortTextLocationPriority, "foo") }, new LSP.CompletionItem { Label = "foo", InsertText = "foo(f) {\n},", SortText = LS.SortBelow(LS.ObjectLiteralPropertySortText(LS.SortTextLocationPriority, "foo")), Data = new LSP.CompletionItemData { Source = "ObjectLiteralMethodSnippet/" }, LabelDetails = new LSP.CompletionItemLabelDetails { Detail = "(f)" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithObjectLiteralMethodSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
