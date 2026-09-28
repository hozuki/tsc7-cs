// ported from: internal/fourslash/tests/incrementalParsingTopLevelAwait2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IncrementalParsingTopLevelAwait2Case
{
    public static void TestIncrementalParsingTopLevelAwait2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: esnext\n// @module: esnext\n// @Filename: ./foo.ts\nexport {};\n/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.GoToMarker(t, "1");
                f.Insert(t, "await(1);");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.ReplaceLine(t, 1, "");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
