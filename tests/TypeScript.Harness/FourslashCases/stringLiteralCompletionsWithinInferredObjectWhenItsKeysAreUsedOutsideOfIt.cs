// ported from: internal/fourslash/tests/stringLiteralCompletionsWithinInferredObjectWhenItsKeysAreUsedOutsideOfIt_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StringLiteralCompletionsWithinInferredObjectWhenItsKeysAreUsedOutsideOfItCase
{
    public static void TestStringLiteralCompletionsWithinInferredObjectWhenItsKeysAreUsedOutsideOfIt(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ndeclare function createMachine<T>(config: {\n  initial: keyof T;\n  states: {\n    [K in keyof T]: {\n      on?: Record<string, keyof T>;\n    };\n  };\n}): void;\n\ncreateMachine({\n  initial: \"a\",\n  states: {\n    a: {\n      on: {\n        NEXT: \"/*1*/\",\n      },\n    },\n    b: {\n      on: {\n        NEXT: \"/*2*/\",\n      },\n    },\n  },\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a", "b"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
