// ported from: internal/fourslash/tests/augmentedTypesModule2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AugmentedTypesModule2Case
{
    public static void TestAugmentedTypesModule2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function /*11*/m2f(x: number) { };\nnamespace m2f { export interface I { foo(): void } }\nvar x: m2f./*1*/\nvar /*2*/r = m2f/*3*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "11", "function m2f(x: number): void", "");
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["I"] } });
                f.Insert(t, "I.");
                f.VerifyCompletions(t, null!, null!);
                f.Backspace(t, 1);
                f.VerifyQuickInfoAt(t, "2", "var r: (x: number) => void", "");
                f.GoToMarker(t, "3");
                f.Insert(t, "(");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "m2f(x: number): void" });
            }
            finally
            {
                done();
            }
        });
    }

}
