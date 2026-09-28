// ported from: internal/fourslash/tests/contextualTypingGenericFunction1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ContextualTypingGenericFunction1Case
{
    public static void TestContextualTypingGenericFunction1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var obj: { f<T>(x: T): T } = { f: <S>(/*1*/x) => x };\nvar obj2: <T>(x: T) => T = <S>(/*2*/x) => x;\n\nclass C<T> {\n    obj: <T>(x: T) => T\n}\nvar c = new C();\nc.obj = <S>(/*3*/x) => x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) x: any", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) x: any", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) x: any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
