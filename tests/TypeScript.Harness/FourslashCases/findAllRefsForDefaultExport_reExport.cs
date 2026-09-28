// ported from: internal/fourslash/tests/findAllRefsForDefaultExport_reExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForDefaultExport_reExportCase
{
    public static void TestFindAllRefsForDefaultExport_reExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /export.ts\nconst /*0*/foo = 1;\nexport default /*1*/foo;\n// @Filename: /re-export.ts\nexport { /*2*/default } from \"./export\";\n// @Filename: /re-export-dep.ts\nimport /*3*/fooDefault from \"./re-export\";"u8);
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
