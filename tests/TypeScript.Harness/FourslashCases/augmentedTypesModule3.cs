// ported from: internal/fourslash/tests/augmentedTypesModule3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AugmentedTypesModule3Case
{
    public static void TestAugmentedTypesModule3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function m2g() { };\nnamespace m2g { export class C { foo(x: number) { } } }\nvar x: m2g./*1*/;\nvar /*2*/r = m2g/*3*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["C"] } });
                f.Insert(t, "C.");
                f.VerifyCompletions(t, null!, null!);
                f.Backspace(t, 1);
                f.VerifyQuickInfoAt(t, "2", "var r: typeof m2g", "");
                f.GoToMarker(t, "3");
                f.Insert(t, "(");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "m2g(): void" });
            }
            finally
            {
                done();
            }
        });
    }

}
