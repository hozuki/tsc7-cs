// ported from: internal/fourslash/tests/completionListsStringLiteralTypeAsIndexedAccessTypeObject_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListsStringLiteralTypeAsIndexedAccessTypeObjectCase
{
    public static void TestCompletionListsStringLiteralTypeAsIndexedAccessTypeObject(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let firstCase: \"a/*case_1*/\"[\"foo\"]\nlet secondCase: \"b/*case_2*/\"[\"bar\"]\nlet thirdCase: \"c/*case_3*/\"[\"baz\"]\nlet fourthCase: \"en/*case_4*/\"[\"qux\"]\ninterface Foo {\n bar: string;\n qux: string;\n}\nlet fifthCase: Foo[\"b/*case_5*/\"]\nlet sixthCase: Foo[\"qu/*case_6*/\"]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["case_1", "case_2", "case_3", "case_4"] }, null!);
                f.VerifyCompletions(t, "case_5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "bar" }] } });
                f.VerifyCompletions(t, "case_6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "qux" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
