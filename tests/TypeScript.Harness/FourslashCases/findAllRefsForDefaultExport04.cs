// ported from: internal/fourslash/tests/findAllRefsForDefaultExport04_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForDefaultExport04Case
{
    public static void TestFindAllRefsForDefaultExport04(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nconst /*0*/a = 0;\nexport /*1*/default /*2*/a;\n// @Filename: /b.ts\nimport /*3*/a from \"./a\";\n/*4*/a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "2", "1", "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
