// ported from: internal/fourslash/tests/parameterWithDestructuring_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ParameterWithDestructuringCase
{
    public static void TestParameterWithDestructuring(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const result = [{ a: 'hello' }]\n    .map(({ /*1*/a }) => /*2*/a)\n    .map(a => a);\n\nconst f1 = (a: (b: string[]) => void) => {};\nf1(([a, b]) => { /*3*/a.charAt(0); });\n\nfunction f2({/*4*/a }: { a: string; }, [/*5*/b]: [string]) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "5", "(parameter) b: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
