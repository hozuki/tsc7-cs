// ported from: internal/fourslash/tests/contextuallyTypedParameters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ContextuallyTypedParametersCase
{
    public static void TestContextuallyTypedParameters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function foo(cb: (this: any, x: number, y: string, z: boolean) => void): void;\n\nfoo(function(this, a, ...args) {\n    a/*10*/;\n    args/*11*/;\n});\n\nfoo(function(this, a, b, ...args) {\n    a/*20*/;\n    b/*21*/;\n    args/*22*/;\n});\n\nfoo(function(this, a, b, c, ...args) {\n    a/*30*/;\n    b/*31*/;\n    c/*32*/;\n    args/*33*/;\n});\n\nfoo(function(a, ...args) {\n    a/*40*/;\n    args/*41*/;\n});\n\nfoo(function(a, b, ...args) {\n    a/*50*/;\n    b/*51*/;\n    args/*52*/;\n});\n\nfoo(function(a, b, c, ...args) {\n    a/*60*/;\n    b/*61*/;\n    c/*62*/;\n    args/*63*/;\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "10", "(parameter) a: number", "");
                f.VerifyQuickInfoAt(t, "11", "(parameter) args: [y: string, z: boolean]", "");
                f.VerifyQuickInfoAt(t, "20", "(parameter) a: number", "");
                f.VerifyQuickInfoAt(t, "21", "(parameter) b: string", "");
                f.VerifyQuickInfoAt(t, "22", "(parameter) args: [z: boolean]", "");
                f.VerifyQuickInfoAt(t, "30", "(parameter) a: number", "");
                f.VerifyQuickInfoAt(t, "31", "(parameter) b: string", "");
                f.VerifyQuickInfoAt(t, "32", "(parameter) c: boolean", "");
                f.VerifyQuickInfoAt(t, "33", "(parameter) args: []", "");
                f.VerifyQuickInfoAt(t, "40", "(parameter) a: number", "");
                f.VerifyQuickInfoAt(t, "41", "(parameter) args: [y: string, z: boolean]", "");
                f.VerifyQuickInfoAt(t, "50", "(parameter) a: number", "");
                f.VerifyQuickInfoAt(t, "51", "(parameter) b: string", "");
                f.VerifyQuickInfoAt(t, "52", "(parameter) args: [z: boolean]", "");
                f.VerifyQuickInfoAt(t, "60", "(parameter) a: number", "");
                f.VerifyQuickInfoAt(t, "61", "(parameter) b: string", "");
                f.VerifyQuickInfoAt(t, "62", "(parameter) c: boolean", "");
                f.VerifyQuickInfoAt(t, "63", "(parameter) args: []", "");
            }
            finally
            {
                done();
            }
        });
    }

}
