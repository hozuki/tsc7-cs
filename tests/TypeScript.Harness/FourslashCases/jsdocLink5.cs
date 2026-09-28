// ported from: internal/fourslash/tests/jsdocLink5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocLink5Case
{
    public static void TestJsdocLink5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function g() { }\n/**\n * {@link g()} {@link g() } {@link g ()} {@link g () 0} {@link g()1} {@link g() 2}\n * {@link u()} {@link u() } {@link u ()} {@link u () 0} {@link u()1} {@link u() 2}\n */\nfunction f(x) {\n}\nf/*3*/()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
