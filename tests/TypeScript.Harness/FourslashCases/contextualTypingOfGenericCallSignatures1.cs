// ported from: internal/fourslash/tests/contextualTypingOfGenericCallSignatures1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ContextualTypingOfGenericCallSignatures1Case
{
    public static void TestContextualTypingOfGenericCallSignatures1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var f24: {\n   <T, U>(x: T): U\n};\n// x should not be contextually typed \nvar f24 = (/**/x) => { return 1 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(parameter) x: any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
