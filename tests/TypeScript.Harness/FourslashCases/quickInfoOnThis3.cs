// ported from: internal/fourslash/tests/quickInfoOnThis3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnThis3Case
{
    public static void TestQuickInfoOnThis3(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Restricted {\n    n: number;\n}\nfunction implicitAny(x: number): void {\n    return th/*1*/is;\n}\nfunction explicitVoid(th/*2*/is: void, x: number): void {\n    return th/*3*/is;\n}\nfunction explicitInterface(th/*4*/is: Restricted): void {\n    console.log(thi/*5*/s);\n}\nfunction explicitLiteral(th/*6*/is: { n: number }): void {\n    console.log(th/*7*/is);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "any", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) this: void", "");
                f.VerifyQuickInfoAt(t, "3", "this: void", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) this: Restricted", "");
                f.VerifyQuickInfoAt(t, "5", "this: Restricted", "");
                f.VerifyQuickInfoAt(t, "6", "(parameter) this: {\n    n: number;\n}", "");
                f.VerifyQuickInfoAt(t, "7", "this: {\n    n: number;\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
