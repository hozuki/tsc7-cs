// ported from: internal/fourslash/tests/quickInfoOnPropDeclaredUsingIndexSignatureOnInterfaceWithBase_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnPropDeclaredUsingIndexSignatureOnInterfaceWithBaseCase
{
    public static void TestQuickInfoOnPropDeclaredUsingIndexSignatureOnInterfaceWithBase(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface P {}\ninterface B extends P {\n  [k: string]: number;\n}\ndeclare const b: B;\nb.t/*1*/est = 10;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(index) B[string]: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
