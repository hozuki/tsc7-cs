// ported from: internal/fourslash/tests/findAllRefsObjectBindingElementPropertyName06_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsObjectBindingElementPropertyName06Case
{
    public static void TestFindAllRefsObjectBindingElementPropertyName06(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    /*0*/property1: number;\n    property2: string;\n}\n\nvar elems: I[];\nfor (let { /*1*/property1: p } of elems) {\n}\nfor (let { /*2*/property1 } of elems) {\n}\nfor (var { /*3*/property1: p1 } of elems) {\n}\nvar p2;\nfor ({ /*4*/property1 : p2 } of elems) {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "3", "4", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
