// ported from: internal/fourslash/tests/findAllRefsPropertyContextuallyTypedByTypeParam01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsPropertyContextuallyTypedByTypeParam01Case
{
    public static void TestFindAllRefsPropertyContextuallyTypedByTypeParam01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo {\n    /*1*/a: string;\n}\nclass C<T extends IFoo> {\n    method() {\n        var x: T = {\n            a: \"\"\n        };\n        x.a;\n    }\n}\n\n\nvar x: IFoo = {\n    a: \"ss\"\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
