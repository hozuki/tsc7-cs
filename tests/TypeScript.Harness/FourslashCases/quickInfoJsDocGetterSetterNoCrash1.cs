// ported from: internal/fourslash/tests/quickInfoJsDocGetterSetterNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocGetterSetterNoCrash1Case
{
    public static void TestQuickInfoJsDocGetterSetterNoCrash1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A implements A {\n  get x(): string { return \"\" }\n}\nconst e = new A()\ne.x/*1*/\n\nclass B implements B {\n  set x(v: string) {}\n}\nconst f = new B()\nf.x/*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) A.x: string", "");
                f.VerifyQuickInfoAt(t, "2", "(property) B.x: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
