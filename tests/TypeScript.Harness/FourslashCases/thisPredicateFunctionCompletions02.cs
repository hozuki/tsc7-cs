// ported from: internal/fourslash/tests/thisPredicateFunctionCompletions02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ThisPredicateFunctionCompletions02Case
{
    public static void TestThisPredicateFunctionCompletions02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Sundries {\n    broken: boolean;\n}\n\ninterface Supplies {\n    spoiled: boolean;\n}\n\ninterface Crate<T> {\n    contents: T;\n    isSundries(): this is Crate<Sundries>;\n    isSupplies(): this is Crate<Supplies>;\n    isPackedTight(): this is (this & {extraContents: T});\n}\nconst crate: Crate<any>;\nif (crate.isPackedTight()) {\n    crate./*1*/;\n}\nif (crate.isSundries()) {\n    crate.contents./*2*/;\n    if (crate.isPackedTight()) {\n        crate./*3*/;\n    }\n}\nif (crate.isSupplies()) {\n    crate.contents./*4*/;\n    if (crate.isPackedTight()) {\n        crate./*5*/;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "3", "5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["contents", "extraContents", "isPackedTight", "isSundries", "isSupplies"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["broken"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["spoiled"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
