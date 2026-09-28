// ported from: internal/fourslash/tests/findAllRefsParameterPropertyDeclaration_inheritance_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsParameterPropertyDeclaration_inheritanceCase
{
    public static void TestFindAllRefsParameterPropertyDeclaration_inheritance(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n\tconstructor(public /*0*/x: string) {\n\t\t/*1*/x;\n\t}\n}\nclass D extends C {\n\tconstructor(public /*2*/x: string) {\n\t\tsuper(/*3*/x);\n\t}\n}"u8);
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
