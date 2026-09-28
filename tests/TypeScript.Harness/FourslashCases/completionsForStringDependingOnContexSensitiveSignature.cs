// ported from: internal/fourslash/tests/completionsForStringDependingOnContexSensitiveSignature_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsForStringDependingOnContexSensitiveSignatureCase
{
    public static void TestCompletionsForStringDependingOnContexSensitiveSignature(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n\ntype ActorRef<TEvent extends { type: string }> = {\n  send: (ev: TEvent) => void\n}\n\ntype Action<TContext> = {\n  (ctx: TContext): void\n}\n\ntype Config<TContext> = {\n  entry: Action<TContext>\n}\n\ndeclare function createMachine<TContext>(config: Config<TContext>): void\n\ntype EventFrom<T> = T extends ActorRef<infer TEvent> ? TEvent : never\n\ndeclare function sendTo<\n  TContext,\n  TActor extends ActorRef<any>\n>(\n  actor: ((ctx: TContext) => TActor),\n  event: EventFrom<TActor>\n): Action<TContext>\n\ncreateMachine<{\n  child: ActorRef<{ type: \"EVENT\" }>;\n}>({\n  entry: sendTo((ctx) => ctx.child, { type: /*1*/ }),\n});\n\ncreateMachine<{\n  child: ActorRef<{ type: \"EVENT\" }>;\n}>({\n  entry: sendTo((ctx) => ctx.child, { type: \"/*2*/\" }),\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["\"EVENT\""] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["EVENT"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
