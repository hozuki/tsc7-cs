// ported from: internal/fourslash/tests/findAllRefsReExportStar_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsReExportStarCase
{
    public static void TestFindAllRefsReExportStar(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport function /*0*/foo(): void {}\n// @Filename: /b.ts\nexport * from \"./a\";\n// @Filename: /c.ts\nimport { /*1*/foo } from \"./b\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "0", "1");
            }
            finally
            {
                done();
            }
        });
    }

}
