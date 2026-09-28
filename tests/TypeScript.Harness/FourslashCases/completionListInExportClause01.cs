// ported from: internal/fourslash/tests/completionListInExportClause01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInExportClause01Case
{
    public static void TestCompletionListInExportClause01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: m1.ts\nexport var foo: number = 1;\nexport function bar() { return 10; }\nexport function baz() { return 10; }\n// @Filename: m2.ts\nexport {/*1*/, /*2*/ from \"./m1\"\nexport {/*3*/} from \"./m1\"\nexport {foo,/*4*/ from \"./m1\"\nexport {bar as /*5*/, /*6*/ from \"./m1\"\nexport {foo, bar, baz as b,/*7*/} from \"./m1\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "baz", "foo", new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "baz", new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "5", null!);
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["baz", "foo", new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, "7", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
