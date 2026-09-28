// ported from: internal/fourslash/tests/quickInfoTypeOfThisInStatics_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoTypeOfThisInStaticsCase
{
    public static void TestQuickInfoTypeOfThisInStatics(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    static foo() {\n        var /*1*/r = this;\n    }\n    static get x() {\n        var /*2*/r = this;\n        return 1;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(local var) r: typeof C", "");
                f.VerifyQuickInfoAt(t, "2", "(local var) r: typeof C", "");
            }
            finally
            {
                done();
            }
        });
    }

}
