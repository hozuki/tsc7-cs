// ported from: internal/fourslash/tests/quickInfoInFunctionTypeReference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoInFunctionTypeReferenceCase
{
    public static void TestQuickInfoInFunctionTypeReference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function map(fn: (variab/*1*/le1: string) => void) {\n}\nvar x = <{ (fn: (va/*2*/riable2: string) => void, a: string): void; }> () => { };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) variable1: string", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) variable2: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
