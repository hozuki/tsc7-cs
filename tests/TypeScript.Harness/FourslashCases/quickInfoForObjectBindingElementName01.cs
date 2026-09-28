// ported from: internal/fourslash/tests/quickInfoForObjectBindingElementName01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForObjectBindingElementName01Case
{
    public static void TestQuickInfoForObjectBindingElementName01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    property1: number;\n    property2: string;\n}\n\nvar foo: I;\nvar { /**/property1 } = foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoAt(t, "", "var property1: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
