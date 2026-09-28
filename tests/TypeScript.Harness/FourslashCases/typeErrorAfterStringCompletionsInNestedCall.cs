// ported from: internal/fourslash/tests/typeErrorAfterStringCompletionsInNestedCall_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TypeErrorAfterStringCompletionsInNestedCallCase
{
    public static void TestTypeErrorAfterStringCompletionsInNestedCall(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @stableTypeOrdering: true\n// @strict: true\n\ntype GreetingEvent =\n  | { type: \"MORNING\" }\n  | { type: \"LUNCH_TIME\" }\n  | { type: \"ALOHA\" };\n\ninterface RaiseActionObject<TEvent extends { type: string }> {\n  type: \"raise\";\n  event: TEvent;\n}\n\ndeclare function raise<TEvent extends { type: string }>(\n  ev: TEvent\n): RaiseActionObject<TEvent>;\n\ndeclare function createMachine<TEvent extends { type: string }>(config: {\n  actions: RaiseActionObject<TEvent>;\n}): void;\n\ncreateMachine<GreetingEvent>({\n  [|/*error*/actions|]: raise({ type: \"ALOHA/*1*/\" }),\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "x");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["ALOHA", "ALOHAx", "LUNCH_TIME", "MORNING"] } });
                f.VerifyNonSuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(2322) }, Message = new LSP.StringOrMarkupContent { String = "Type 'RaiseActionObject<{ type: \"ALOHAx\"; }>' is not assignable to type 'RaiseActionObject<GreetingEvent>'.\n  Type '{ type: \"ALOHAx\"; }' is not assignable to type 'GreetingEvent'.\n    Type '{ type: \"ALOHAx\"; }' is not assignable to type '{ type: \"ALOHA\"; }'.\n      Types of property 'type' are incompatible.\n        Type '\"ALOHAx\"' is not assignable to type '\"ALOHA\"'." } }]);
            }
            finally
            {
                done();
            }
        });
    }

}
