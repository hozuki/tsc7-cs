// ported from: internal/fourslash/tests/findAllRefsDestructureGetter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsDestructureGetterCase
{
    public static void TestFindAllRefsDestructureGetter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Test {\n    get /*x0*/x() { return 0; }\n\n    set /*y0*/y(a: number) {}\n}\nconst { /*x1*/x, /*y1*/y } = new Test();\n/*x2*/x; /*y2*/y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "x0", "x1", "x2", "y0", "y1", "y2");
            }
            finally
            {
                done();
            }
        });
    }

}
