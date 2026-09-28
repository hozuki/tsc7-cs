// ported from: internal/fourslash/tests/completionInNamedImportLocation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionInNamedImportLocationCase
{
    public static void TestCompletionInNamedImportLocation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file.ts\nexport var x = 10;\nexport var y = 10;\nexport { x as await, x as interface, x as unique };\nexport default class C {\n}\n// @Filename: a.ts\nimport { /*1*/ } from \"./file\";\nimport { x, /*2*/ } from \"./file\";\nimport { x, y, /*3*/ } from \"./file\";\nimport { x, y, await as await_, /*4*/ } from \"./file\";\nimport { x, y, await as await_, interface as interface_, /*5*/ } from \"./file\";\nimport { x, y, await as await_, interface as interface_, unique, /*6*/ } from \"./file\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "a.ts");
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "await", InsertText = "await as await_" }, new LSP.CompletionItem { Label = "interface", InsertText = "interface as interface_" }, new LSP.CompletionItem { Label = "unique" }, new LSP.CompletionItem { Label = "x", Detail = "var x: number" }, new LSP.CompletionItem { Label = "y", Detail = "var y: number" }, new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "await", InsertText = "await as await_" }, new LSP.CompletionItem { Label = "interface", InsertText = "interface as interface_" }, new LSP.CompletionItem { Label = "unique" }, new LSP.CompletionItem { Label = "y", Detail = "var y: number" }, new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "await", InsertText = "await as await_" }, new LSP.CompletionItem { Label = "interface", InsertText = "interface as interface_" }, new LSP.CompletionItem { Label = "unique" }, new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "interface", InsertText = "interface as interface_" }, new LSP.CompletionItem { Label = "unique" }, new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "unique" }, new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [] } });
            }
            finally
            {
                done();
            }
        });
    }

}
