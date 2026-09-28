// ported from: internal/fourslash/tests/importNameCodeFixNewImportFile1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportFile1Case
{
    public static void TestImportNameCodeFixNewImportFile1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|/// <reference path=\"./tripleSlashReference.ts\" />\nf1/*0*/();|]\n// @Filename: Module.ts\nexport function f1() {}\nexport var v1 = 5;\n// @Filename: tripleSlashReference.ts\nvar x = 5;/*dummy*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["/// <reference path=\"./tripleSlashReference.ts\" />\n\nimport { f1 } from \"./Module\";\n\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
