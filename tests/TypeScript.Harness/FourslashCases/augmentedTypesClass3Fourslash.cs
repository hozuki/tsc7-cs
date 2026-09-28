// ported from: internal/fourslash/tests/augmentedTypesClass3Fourslash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AugmentedTypesClass3FourslashCase
{
    public static void TestAugmentedTypesClass3Fourslash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class c/*1*/5b { public foo() { } }\nnamespace c/*2*/5b { export var y = 2; } // should be ok\n/*3*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "class c5b\nnamespace c5b", "");
                f.VerifyQuickInfoAt(t, "2", "class c5b\nnamespace c5b", "");
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "c5b", Detail = "class c5b\nnamespace c5b" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
