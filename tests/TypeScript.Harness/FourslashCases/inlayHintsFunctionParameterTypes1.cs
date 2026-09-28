// ported from: internal/fourslash/tests/inlayHintsFunctionParameterTypes1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsFunctionParameterTypes1Case
{
    public static void TestInlayHintsFunctionParameterTypes1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type F1 = (a: string, b: number) => void\nconst f1: F1 = (a, b) => { }\nconst f2: F1 = (a, b: number) => { }\nfunction foo1 (cb: (a: string) => void) {}\nfoo1((a) => { })\nfunction foo2 (cb: (a: Exclude<1 | 2 | 3, 1>) => void) {}\nfoo2((a) => { })\nfunction foo3 (a: (b: (c: (d: Exclude<1 | 2 | 3, 1>) => void) => void) => void) {}\nfoo3(a => {\n    a(d => {})\n})\nfunction foo4<T>(v: T, a: (v: T) => void) {}\nfoo4(1, a => { })\ntype F2 = (a: {\n    a: number\n    b: string\n}) => void\nconst foo5: F2 = (a) => { }"u8);
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
