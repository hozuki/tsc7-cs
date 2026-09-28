// ported from: internal/fourslash/tests/completionDetailsOfContextSensitiveParameterNoCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionDetailsOfContextSensitiveParameterNoCrashCase
{
    public static void TestCompletionDetailsOfContextSensitiveParameterNoCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ntype __ = never;\n\ninterface CurriedFunction1<T1, R> {\n    (): CurriedFunction1<T1, R>;\n    (t1: T1): R;\n}\ninterface CurriedFunction2<T1, T2, R> {\n    (): CurriedFunction2<T1, T2, R>;\n    (t1: T1): CurriedFunction1<T2, R>;\n    (t1: __, t2: T2): CurriedFunction1<T1, R>;\n    (t1: T1, t2: T2): R;\n}\n\ninterface CurriedFunction3<T1, T2, T3, R> {\n    (): CurriedFunction3<T1, T2, T3, R>;\n    (t1: T1): CurriedFunction2<T2, T3, R>;\n    (t1: __, t2: T2): CurriedFunction2<T1, T3, R>;\n    (t1: T1, t2: T2): CurriedFunction1<T3, R>;\n    (t1: __, t2: __, t3: T3): CurriedFunction2<T1, T2, R>;\n    (t1: T1, t2: __, t3: T3): CurriedFunction1<T2, R>;\n    (t1: __, t2: T2, t3: T3): CurriedFunction1<T1, R>;\n    (t1: T1, t2: T2, t3: T3): R;\n}\n\ninterface CurriedFunction4<T1, T2, T3, T4, R> {\n    (): CurriedFunction4<T1, T2, T3, T4, R>;\n    (t1: T1): CurriedFunction3<T2, T3, T4, R>;\n    (t1: __, t2: T2): CurriedFunction3<T1, T3, T4, R>;\n    (t1: T1, t2: T2): CurriedFunction2<T3, T4, R>;\n    (t1: __, t2: __, t3: T3): CurriedFunction3<T1, T2, T4, R>;\n    (t1: __, t2: __, t3: T3): CurriedFunction2<T2, T4, R>;\n    (t1: __, t2: T2, t3: T3): CurriedFunction2<T1, T4, R>;\n    (t1: T1, t2: T2, t3: T3): CurriedFunction1<T4, R>;\n    (t1: __, t2: __, t3: __, t4: T4): CurriedFunction3<T1, T2, T3, R>;\n    (t1: T1, t2: __, t3: __, t4: T4): CurriedFunction2<T2, T3, R>;\n    (t1: __, t2: T2, t3: __, t4: T4): CurriedFunction2<T1, T3, R>;\n    (t1: __, t2: __, t3: T3, t4: T4): CurriedFunction2<T1, T2, R>;\n    (t1: T1, t2: T2, t3: __, t4: T4): CurriedFunction1<T3, R>;\n    (t1: T1, t2: __, t3: T3, t4: T4): CurriedFunction1<T2, R>;\n    (t1: __, t2: T2, t3: T3, t4: T4): CurriedFunction1<T1, R>;\n    (t1: T1, t2: T2, t3: T3, t4: T4): R;\n}\n\ndeclare var curry: {\n    <T1, R>(func: (t1: T1) => R, arity?: number): CurriedFunction1<T1, R>;    \n    <T1, T2, R>(func: (t1: T1, t2: T2) => R, arity?: number): CurriedFunction2<T1, T2, R>;\n    <T1, T2, T3, R>(func: (t1: T1, t2: T2, t3: T3) => R, arity?: number): CurriedFunction3<T1, T2, T3, R>;\n    <T1, T2, T3, T4, R>(func: (t1: T1, t2: T2, t3: T3, t4: T4) => R, arity?: number): CurriedFunction4<T1, T2, T3, T4, R>;\n    (func: (...args: any[]) => any, arity?: number): (...args: any[]) => any;\n    placeholder: __;\n};\n\nexport type StylingFunction = (\n    keys: (string | false | undefined) | (string | false | undefined)[],\n    ...rest: unknown[]\n) => object;\n\ndeclare const getStylingByKeys: (\n    mergedStyling: object,\n    keys: (string | false | undefined) | (string | false | undefined)[],\n    ...args: unknown[]\n) => object;\n\ndeclare var mergedStyling: object;\n\nexport const createStyling: CurriedFunction3<\n    (base16Theme: object) => unknown,\n    object | undefined,\n    object | undefined,\n    StylingFunction\n> = curry<\n    (base16Theme: object) => unknown,\n    object | undefined,\n    object | undefined,\n    StylingFunction\n>(\n    (\n        getStylingFromBase16: (base16Theme: object) => unknown,\n        options: object = {},\n        themeOrStyling: object = {},\n        ...args\n    ): StylingFunction => {\n        return curry(getStylingByKeys, 2)(mergedStyling, .../**/args);\n    },\n    3\n);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
