// ported from: internal/fourslash/tests/importNameCodeFixExistingImport4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixExistingImport4Case
{
    public static void TestImportNameCodeFixExistingImport4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|import d from \"./module\";\nf1/*0*/();|]\n// @Filename: module.ts\nexport function f1() {}\nexport var v1 = 5;\nexport default var d1 = 6;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import d, { f1 } from \"./module\";\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
