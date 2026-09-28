// ported from: internal/fourslash/tests/incrementalParsingDynamicImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IncrementalParsingDynamicImport2Case
{
    public static void TestIncrementalParsingDynamicImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es2015\n// @Filename: ./foo.ts\nexport function bar() { return 1; }\n// @Filename: ./0.ts\n/*1*/ import { bar } from \"./foo\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.GoToMarker(t, "1");
                f.Insert(t, "var x = ");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
