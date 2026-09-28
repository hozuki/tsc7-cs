// ported from: internal/fourslash/tests/quickInfoOnUndefined_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnUndefinedCase
{
    public static void TestQuickInfoOnUndefined(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(a: string) {\n}\nfoo(/*1*/undefined);\nvar x = {\n    undefined: 10\n};\nx./*2*/undefined = 30;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var undefined", "");
                f.VerifyQuickInfoAt(t, "2", "(property) undefined: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
