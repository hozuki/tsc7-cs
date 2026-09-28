// ported from: internal/fourslash/tests/findAllRefsExportAsNamespace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsExportAsNamespaceCase
{
    public static void TestFindAllRefsExportAsNamespace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/a/index.d.ts\nexport function /*0*/f(): void;\nexport as namespace A;\n// @Filename: /b.ts\nimport { /*1*/f } from \"a\";\n// @Filename: /c.ts\nA./*2*/f();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
