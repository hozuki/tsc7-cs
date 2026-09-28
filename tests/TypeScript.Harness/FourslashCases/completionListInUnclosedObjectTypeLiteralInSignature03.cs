// ported from: internal/fourslash/tests/completionListInUnclosedObjectTypeLiteralInSignature03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInUnclosedObjectTypeLiteralInSignature03Case
{
    public static void TestCompletionListInUnclosedObjectTypeLiteralInSignature03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I<TString, TNumber> {\n    [s: string]: TString;\n    [s: number]: TNumber;\n}\n\ndeclare function foo<TString, TNumber>(obj: I<TString, TNumber>): { str: TString/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["I", "TString", "TNumber"], Excludes = ["foo", "obj"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
