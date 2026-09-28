// ported from: internal/fourslash/tests/findAllRefsForComputedProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForComputedPropertiesCase
{
    public static void TestFindAllRefsForComputedProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    [\"/*0*/prop1\"]: () => void;\n}\n\nclass C implements I {\n    [\"/*1*/prop1\"]: any;\n}\n\nvar x: I = {\n    [\"/*2*/prop1\"]: function () { },\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
