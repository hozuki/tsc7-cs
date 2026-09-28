// ported from: internal/fourslash/tests/completionsWithStringReplacementMode1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsWithStringReplacementMode1Case
{
    public static void TestCompletionsWithStringReplacementMode1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface TFunction {\n    (_: 'login.title', __?: {}): string;\n    (_: 'login.description', __?: {}): string;\n    (_: 'login.sendEmailAgree', __?: {}): string;\n    (_: 'login.termsOfUse', __?: {}): string;\n    (_: 'login.privacyPolicy', __?: {}): string;\n    (_: 'login.sendEmailButton', __?: {}): string;\n    (_: 'login.emailInputPlaceholder', __?: {}): string;\n    (_: 'login.errorWrongEmailTitle', __?: {}): string;\n    (_: 'login.errorWrongEmailDescription', __?: {}): string;\n    (_: 'login.errorGeneralEmailTitle', __?: {}): string;\n    (_: 'login.errorGeneralEmailDescription', __?: {}): string;\n    (_: 'login.loginErrorTitle', __?: {}): string;\n    (_: 'login.loginErrorDescription', __?: {}): string;\n    (_: 'login.openEmailAppErrorTitle', __?: {}): string;\n    (_: 'login.openEmailAppErrorDescription', __?: {}): string;\n    (_: 'login.openEmailAppErrorConfirm', __?: {}): string;\n}\nconst f: TFunction = (() => {}) as any;\nf('[|login./**/|]')"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "login.description", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.description", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.emailInputPlaceholder", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.emailInputPlaceholder", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.errorGeneralEmailDescription", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.errorGeneralEmailDescription", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.errorGeneralEmailTitle", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.errorGeneralEmailTitle", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.errorWrongEmailDescription", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.errorWrongEmailDescription", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.errorWrongEmailTitle", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.errorWrongEmailTitle", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.loginErrorDescription", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.loginErrorDescription", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.loginErrorTitle", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.loginErrorTitle", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.openEmailAppErrorConfirm", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.openEmailAppErrorConfirm", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.openEmailAppErrorDescription", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.openEmailAppErrorDescription", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.openEmailAppErrorTitle", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.openEmailAppErrorTitle", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.privacyPolicy", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.privacyPolicy", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.sendEmailAgree", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.sendEmailAgree", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.sendEmailButton", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.sendEmailButton", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.termsOfUse", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.termsOfUse", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "login.title", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "login.title", Range = f.Ranges()[0].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
