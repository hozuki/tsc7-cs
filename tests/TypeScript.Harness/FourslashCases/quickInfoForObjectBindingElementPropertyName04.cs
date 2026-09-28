// ported from: internal/fourslash/tests/quickInfoForObjectBindingElementPropertyName04_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForObjectBindingElementPropertyName04Case
{
    public static void TestQuickInfoForObjectBindingElementPropertyName04(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Recursive {\n    next?: Recursive;\n    value: any;\n}\n\nfunction f ({ /*1*/next: { /*2*/next: x} }) {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) next: {\n    next: any;\n}", "");
                f.VerifyQuickInfoAt(t, "2", "(property) next: any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
