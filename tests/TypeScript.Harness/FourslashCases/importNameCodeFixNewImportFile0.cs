// ported from: internal/fourslash/tests/importNameCodeFixNewImportFile0_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportFile0Case
{
    public static void TestImportNameCodeFixNewImportFile0(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|f1/*0*/();|]\n// @Filename: jalape\u00F1o.ts\nexport function f1() {}\nexport var v1 = 5;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"./jalape\u00f1o\";\n\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
