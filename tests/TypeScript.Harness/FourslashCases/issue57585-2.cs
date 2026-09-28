// ported from: internal/fourslash/tests/issue57585-2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Issue57585_2Case
{
    public static void TestIssue57585_2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @target: esnext\n// @lib: esnext\ndeclare const EffectTypeId: unique symbol;\n\ntype Covariant<A> = (_: never) => A;\n\ninterface VarianceStruct<out A, out E, out R> {\n  readonly _V: string;\n  readonly _A: Covariant<A>;\n  readonly _E: Covariant<E>;\n  readonly _R: Covariant<R>;\n}\n\ninterface Variance<out A, out E, out R> {\n  readonly [EffectTypeId]: VarianceStruct<A, E, R>;\n}\n\ntype Success<T extends Effect<any, any, any>> = [T] extends [\n  Effect<infer _A, infer _E, infer _R>,\n]\n  ? _A\n  : never;\n\ndeclare const YieldWrapTypeId: unique symbol;\n\nclass YieldWrap<T> {\n  readonly #value: T;\n  constructor(value: T) {\n    this.#value = value;\n  }\n  [YieldWrapTypeId](): T {\n    return this.#value;\n  }\n}\n\ninterface EffectGenerator<T extends Effect<any, any, any>> {\n  next(...args: ReadonlyArray<any>): IteratorResult<YieldWrap<T>, Success<T>>;\n}\n\ninterface Effect<out A, out E = never, out R = never>\n  extends Variance<A, E, R> {\n  [Symbol.iterator](): EffectGenerator<Effect<A, E, R>>;\n}\n\ndeclare const gen: {\n  <Eff extends YieldWrap<Effect<any, any, any>>, AEff>(\n    f: () => Generator<Eff, AEff, never>,\n  ): Effect<\n    AEff,\n    [Eff] extends [never]\n      ? never\n      : [Eff] extends [YieldWrap<Effect<infer _A, infer E, infer _R>>]\n      ? E\n      : never,\n    [Eff] extends [never]\n      ? never\n      : [Eff] extends [YieldWrap<Effect<infer _A, infer _E, infer R>>]\n      ? R\n      : never\n  >;\n};\n\ndeclare const succeed: <A>(value: A) => Effect<A>;\n\ngen(function* () {\n  const a = yield* succeed(1);\n  const b/*1*/ = yield* succeed(2);\n  return a + b;\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const b: number", "");
                f.VerifyNonSuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
