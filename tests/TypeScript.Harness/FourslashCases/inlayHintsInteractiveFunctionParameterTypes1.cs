// ported from: internal/fourslash/tests/inlayHintsInteractiveFunctionParameterTypes1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveFunctionParameterTypes1Case
{
    public static void TestInlayHintsInteractiveFunctionParameterTypes1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" type F1 = (a: string, b: number) => void\n const f1: F1 = (a, b) => { }\n const f2: F1 = (a, b: number) => { }\n function foo1 (cb: (a: string) => void) {}\n foo1((a) => { })\n function foo2 (cb: (a: Exclude<1 | 2 | 3, 1>) => void) {}\n foo2((a) => { })\n function foo3 (a: (b: (c: (d: Exclude<1 | 2 | 3, 1>) => void) => void) => void) {}\n foo3(a => {\n     a(d => {})\n })\n function foo4<T>(v: T, a: (v: T) => void) {}\n foo4(1, a => { })\n type F2 = (a: {\n     a: number\n     b: string\n     readonly c: boolean\n     d?: number\n     e(): string\n     f?(): boolean\n     g<T>(): T\n     h?<X, Y>(x: X): Y\n     <X, Y>(x: X): Y\n     [i: string]: number\n }) => void\n const foo5: F2 = (a) => { }\n type F3 = (a: {\n     (): 42\n }) => void\n const foo6: F3 = (a) => { }\ninterface Thing {}\nfunction foo4(callback: (thing: Thing) => void) {}\nfoo4(p => {})\n type F4 = (a: {\n     [i in string]: number\n }) => void\n const foo5: F4 = (a) => { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayFunctionParameterTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
