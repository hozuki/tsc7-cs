// ported from: internal/fourslash/tests/quickInfoForContextuallyTypedIife_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForContextuallyTypedIifeCase
{
    public static void TestQuickInfoForContextuallyTypedIife(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("(({ q/*1*/, qq/*2*/ }, x/*3*/, { p/*4*/ }) => {\n    var s: number = q/*5*/;\n    var t: number = qq/*6*/;\n    var u: number = p/*7*/;\n    var v: number = x/*8*/;\n    return q; })({ q: 13, qq: 12 }, 1, { p: 14 });\n((a/*9*/, b/*10*/, c/*11*/) => [a/*12*/,b/*13*/,c/*14*/])(\"foo\", 101, false);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) q: number", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) qq: number", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) p: number", "");
                f.VerifyQuickInfoAt(t, "5", "(parameter) q: number", "");
                f.VerifyQuickInfoAt(t, "6", "(parameter) qq: number", "");
                f.VerifyQuickInfoAt(t, "7", "(parameter) p: number", "");
                f.VerifyQuickInfoAt(t, "8", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "9", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "10", "(parameter) b: number", "");
                f.VerifyQuickInfoAt(t, "11", "(parameter) c: boolean", "");
                f.VerifyQuickInfoAt(t, "12", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "13", "(parameter) b: number", "");
                f.VerifyQuickInfoAt(t, "14", "(parameter) c: boolean", "");
            }
            finally
            {
                done();
            }
        });
    }

}
