// ported from: internal/fourslash/tests/completionListInObjectLiteral5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInObjectLiteral5Case
{
    public static void TestCompletionListInObjectLiteral5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const o = 'something' \nconst obj = {\n    prop: o/*1*/,\n    pro() {\n        const obj1 = {\n            p:{\n                s: {\n                    h: {\n                       hh: o/*2*/\n                    },\n                    someFun() {\n                        o/*3*/\n                    }\n                }\n            }\n        }\n    },\n    o/*4*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["o"], Excludes = ["obj"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["o", "obj"], Excludes = ["obj1"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["o", "obj", "obj1"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["o"], Excludes = ["obj"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
