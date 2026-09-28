// ported from: internal/fourslash/tests/constQuickInfoAndCompletionList_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ConstQuickInfoAndCompletionListCase
{
    public static void TestConstQuickInfoAndCompletionList(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const /*1*/a = 10;\nvar x = /*2*/a;\n/*3*/\nfunction foo() {\n    const /*4*/b = 20;\n    var y = /*5*/b;\n    var z = /*6*/a;\n    /*7*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "const a: 10" }], Excludes = ["b"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "const a: 10" }], Excludes = ["b"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["5", "6"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "const a: 10" }, new LSP.CompletionItem { Label = "b", Detail = "const b: 20" }] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "const a: 10" }, new LSP.CompletionItem { Label = "b", Detail = "const b: 20" }] } });
                f.VerifyQuickInfoAt(t, "1", "const a: 10", "");
                f.VerifyQuickInfoAt(t, "2", "const a: 10", "");
                f.VerifyQuickInfoAt(t, "4", "const b: 20", "");
                f.VerifyQuickInfoAt(t, "5", "const b: 20", "");
                f.VerifyQuickInfoAt(t, "6", "const a: 10", "");
            }
            finally
            {
                done();
            }
        });
    }

}
