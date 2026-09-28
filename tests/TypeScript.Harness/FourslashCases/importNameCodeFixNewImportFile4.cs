// ported from: internal/fourslash/tests/importNameCodeFixNewImportFile4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportFile4Case
{
    public static void TestImportNameCodeFixNewImportFile4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|let t: A/*0*/.B.I;|]\n// @Filename: ./module.ts\nexport namespace A {\n   export namespace B {\n       export interface I { }\n   }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { A } from \"./module\";\n\nlet t: A.B.I;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
