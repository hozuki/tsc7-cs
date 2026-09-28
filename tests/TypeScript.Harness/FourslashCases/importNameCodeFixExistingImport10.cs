// ported from: internal/fourslash/tests/importNameCodeFixExistingImport10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixExistingImport10Case
{
    public static void TestImportNameCodeFixExistingImport10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import [|{\n    v1,\n    v2\n}|] from \"./module\";\nf1/*0*/();\n// @Filename: module.ts\nexport function f1() {}\nexport var v1 = 5;\nexport var v2 = 5;\nexport var v3 = 5;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["{\n    f1,\n    v1,\n    v2\n}"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
