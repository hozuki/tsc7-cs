// ported from: internal/fourslash/tests/importNameCodeFixExistingImport7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixExistingImport7Case
{
    public static void TestImportNameCodeFixExistingImport7(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import [|{ v1 }|] from \"../other_dir/module\";\nf1/*0*/();\n// @Filename: ../other_dir/module.ts\nexport var v1 = 5;\nexport function f1();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["{ f1, v1 }"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
