// ported from: internal/fourslash/tests/superInsideInnerClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SuperInsideInnerClassCase
{
    public static void TestSuperInsideInnerClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base {\n\tconstructor(n: number) {\n\t}\n}\nclass Derived extends Base {\n\tconstructor() {\n\t\tclass Nested {\n\t\t\t[super(/*1*/)] = 11111\n\t\t}\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoSignatureHelpForMarkers(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
