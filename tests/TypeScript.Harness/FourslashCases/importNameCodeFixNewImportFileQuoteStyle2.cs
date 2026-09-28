// ported from: internal/fourslash/tests/importNameCodeFixNewImportFileQuoteStyle2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportFileQuoteStyle2Case
{
    public static void TestImportNameCodeFixNewImportFileQuoteStyle2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|import m2 = require('./module2');\n\nf1/*0*/();|]\n// @Filename: module1.ts\nexport function f1() {}\n// @Filename: module2.ts\nexport var v2 = 6;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { f1 } from './module1';\nimport m2 = require('./module2');\n\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
