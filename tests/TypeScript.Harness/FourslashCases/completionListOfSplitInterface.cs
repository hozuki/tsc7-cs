// ported from: internal/fourslash/tests/completionListOfSplitInterface_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListOfSplitInterfaceCase
{
    public static void TestCompletionListOfSplitInterface(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A {\n    a: number;\n}\ninterface I extends A {\n    i1: number;\n}\ninterface I1 extends A {\n    i11: number;\n}\ninterface B {\n    b: number;\n}\ninterface B1 {\n    b1: number;\n}\ninterface I extends B {\n    i2: number;\n}\ninterface I1 extends B, B1 {\n    i12: number;\n}\ninterface C {\n    c: number;\n}\ninterface I extends C {\n    i3: number;\n}\nvar ci: I;\nci./*1*/b;\nvar ci1: I1;\nci1./*2*/b;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["i1", "i2", "i3", "a", "b", "c"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["i11", "i12", "a", "b", "b1"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
