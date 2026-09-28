// ported from: internal/fourslash/tests/completionListInNamedClassExpressionWithShadowing_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInNamedClassExpressionWithShadowingCase
{
    public static void TestCompletionListInNamedClassExpressionWithShadowing(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class myClass { /*0*/ }\n/*1*/\nvar x = class myClass {\n   getClassName (){\n       m/*2*/\n   }\n   /*3*/\n}\nvar y = class {\n   getSomeName() {\n       /*4*/\n   }\n   /*5*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["myClass"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "myClass", Detail = "class myClass", Kind = LSP.CompletionItemKind.Class }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "myClass", Detail = "(local class) myClass", Kind = LSP.CompletionItemKind.Property }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3", "5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["myClass"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
