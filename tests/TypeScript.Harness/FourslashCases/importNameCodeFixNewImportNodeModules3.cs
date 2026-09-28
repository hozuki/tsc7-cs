// ported from: internal/fourslash/tests/importNameCodeFixNewImportNodeModules3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportNodeModules3Case
{
    public static void TestImportNameCodeFixNewImportNodeModules3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n[|f1/*0*/();|]\n// @Filename: /node_modules/@types/random/index.d.ts\nexport var v1 = 5;\nexport function f1();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"random\";\n\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
