// ported from: internal/fourslash/tests/findAllRefsRedeclaredPropertyInDerivedInterface_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsRedeclaredPropertyInDerivedInterfaceCase
{
    public static void TestFindAllRefsRedeclaredPropertyInDerivedInterface(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\ninterface A {\n    readonly /*0*/x: number | string;\n}\ninterface B extends A {\n    readonly /*1*/x: number;\n}\nconst a: A = { /*2*/x: 0 };\nconst b: B = { /*3*/x: 0 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
