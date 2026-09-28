// ported from: internal/fourslash/tests/completionForStringLiteral_quotePreference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteral_quotePreferenceCase
{
    public static void TestCompletionForStringLiteral_quotePreference(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("enum A {\n    A,\n    B,\n    C\n}\ninterface B {\n    a: keyof typeof A;\n}\nconst b: B = {\n    a: /**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "'A'" }, new LSP.CompletionItem { Label = "'B'" }, new LSP.CompletionItem { Label = "'C'" }] }, UserPreferences = new LSUP.UserPreferences { QuotePreference = new LSUP.QuotePreference("single") } });
            }
            finally
            {
                done();
            }
        });
    }

}
