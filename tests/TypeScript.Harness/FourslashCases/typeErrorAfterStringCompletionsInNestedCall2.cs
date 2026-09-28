// ported from: internal/fourslash/tests/typeErrorAfterStringCompletionsInNestedCall2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TypeErrorAfterStringCompletionsInNestedCall2Case
{
    public static void TestTypeErrorAfterStringCompletionsInNestedCall2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @stableTypeOrdering: true\n// @strict: true\n\ntype ActionFunction<\n  TExpressionEvent extends { type: string },\n  out TEvent extends { type: string }\n> = {\n  ({ event }: { event: TExpressionEvent }): void;\n  _out_TEvent?: TEvent;\n};\n\ninterface MachineConfig<TEvent extends { type: string }> {\n  types: {\n    events: TEvent;\n  };\n  on: {\n    [K in TEvent[\"type\"]]?: ActionFunction<\n      Extract<TEvent, { type: K }>,\n      TEvent\n    >;\n  };\n}\n\ndeclare function raise<\n  TExpressionEvent extends { type: string },\n  TEvent extends { type: string }\n>(\n  resolve: ({ event }: { event: TExpressionEvent }) => TEvent\n): {\n  ({ event }: { event: TExpressionEvent }): void;\n  _out_TEvent?: TEvent;\n};\n\ndeclare function createMachine<TEvent extends { type: string }>(\n  config: MachineConfig<TEvent>\n): void;\n\ncreateMachine({\n  types: {\n    events: {} as { type: \"FOO\" } | { type: \"BAR\" },\n  },\n  on: {\n    [|/*error*/FOO|]: raise(({ event }) => {\n      return {\n        type: \"BAR/*1*/\" as const,\n      };\n    }),\n  },\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "x");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["BAR", "FOO"] } });
                f.VerifyBaselineNonSuggestionDiagnostics(t);
            }
            finally
            {
                done();
            }
        });
    }

}
