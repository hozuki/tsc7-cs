// ported from: internal/fourslash/tests/completionsLiteralFromInferenceWithinInferredType2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsLiteralFromInferenceWithinInferredType2Case
{
    public static void TestCompletionsLiteralFromInferenceWithinInferredType2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.tsx\ntype Values<T> = T[keyof T];\n\ntype GetStates<T> = T extends { states: object } ? T[\"states\"] : never;\n\ntype IsNever<T> = [T] extends [never] ? 1 : 0;\n\ntype GetIds<T, Gathered extends string = never> = IsNever<T> extends 1\n  ? Gathered\n  : \"id\" extends keyof T\n  ? GetIds<Values<GetStates<T>>, Gathered | `#${T[\"id\"] & string}`>\n  : GetIds<Values<GetStates<T>>, Gathered>;\n\ntype StateConfig<\n  TStates extends Record<string, StateConfig> = Record<\n    string,\n    StateConfig<any>\n  >,\n  TIds extends string = string\n> = {\n  id?: string;\n  initial?: keyof TStates & string;\n  states?: {\n    [K in keyof TStates]: StateConfig<GetStates<TStates[K]>, TIds>;\n  };\n  on?: Record<string, TIds | `.${keyof TStates & string}`>;\n};\n\ndeclare function createMachine<const T extends StateConfig<GetStates<T>, GetIds<T>>>(\n  config: T\n): void;\n\ncreateMachine({\n  initial: \"child\",\n  states: {\n    child: {\n      initial: \"foo\",\n      states: {\n        foo: {\n          id: \"wow_deep_id\",\n        },\n      },\n    },\n  },\n  on: {\n    EV: \"/*ts*/\",\n  },\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["#wow_deep_id", ".child"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
