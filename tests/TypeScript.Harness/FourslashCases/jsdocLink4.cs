// ported from: internal/fourslash/tests/jsdocLink4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocLink4Case
{
    public static void TestJsdocLink4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare class I {\n  /** {@link I} */\n  bar/*1*/(): void\n}\n/** {@link I} */\nvar n/*2*/ = 1\n/**\n * A real, very serious {@link I to an interface}. Right there.\n * @param x one {@link Pos here too}\n */\nfunction f(x) {\n}\nf/*3*/()\ntype Pos = [number, number]"u8);
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
