// ported from: internal/fourslash/tests/findAllRefsWriteAccess_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsWriteAccessCase
{
    public static void TestFindAllRefsWriteAccess(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Obj {\n    [`/*1*/num`]: number;\n}\n\nlet o: Obj = {\n    [`num`]: 0\n};\n\no = {\n    ['num']: 1\n};\n\no['num'] = 2;\no[`num`] = 3;\n\no['num'];\no[`num`];"u8);
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
