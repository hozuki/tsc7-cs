// ported from: internal/fourslash/tests/quickInfoNestedGenericCalls_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoNestedGenericCallsCase
{
    public static void TestQuickInfoNestedGenericCalls(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n/*1*/m({ foo: /*2*/$(\"foo\") });\nm({ foo: /*3*/$(\"foo\") });\ndeclare const m: <S extends string>(s: { [_ in S]: { $: NoInfer<S> } }) => void\ndeclare const $: <S, T extends S>(s: T) => { $: S }\ntype NoInfer<T> = [T][T extends any ? 0 : never];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const m: <\"foo\">(s: {\n    foo: {\n        $: \"foo\";\n    };\n}) => void", "");
                f.VerifyQuickInfoAt(t, "2", "const $: <unknown, string>(s: string) => {\n    $: unknown;\n}", "");
                f.VerifyQuickInfoAt(t, "3", "const $: <unknown, string>(s: string) => {\n    $: unknown;\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
