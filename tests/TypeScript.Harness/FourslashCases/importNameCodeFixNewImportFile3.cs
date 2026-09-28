// ported from: internal/fourslash/tests/importNameCodeFixNewImportFile3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportFile3Case
{
    public static void TestImportNameCodeFixNewImportFile3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|let t: XXX/*0*/.I;|]\n// @Filename: ./module.ts\nexport namespace XXX {\n   export interface I {\n   }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { XXX } from \"./module\";\n\nlet t: XXX.I;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
