// ported from: internal/fourslash/tests/functionProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FunctionPropertyCase
{
    public static void TestFunctionProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var a = {\n    x(a: number) { }\n};\n\nvar b = {\n    x: function (a: number) { }\n};\n\nvar c = {\n    x: (a: number) => { }\n};\na.x(/*signatureA*/1);\nb.x(/*signatureB*/1);\nc.x(/*signatureC*/1);\na./*completionA*/;\nb./*completionB*/;\nc./*completionC*/;\na./*quickInfoA*/x;\nb./*quickInfoB*/x;\nc./*quickInfoC*/x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "signatureA");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "x(a: number): void" });
                f.GoToMarker(t, "signatureB");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "x(a: number): void" });
                f.GoToMarker(t, "signatureC");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "x(a: number): void" });
                f.VerifyCompletions(t, "completionA", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", Detail = "(method) x(a: number): void" }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["completionB", "completionC"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", Detail = "(property) x: (a: number) => void" }] } });
                f.VerifyQuickInfoAt(t, "quickInfoA", "(method) x(a: number): void", "");
                f.VerifyQuickInfoAt(t, "quickInfoB", "(property) x: (a: number) => void", "");
                f.VerifyQuickInfoAt(t, "quickInfoC", "(property) x: (a: number) => void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
