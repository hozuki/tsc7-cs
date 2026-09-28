// ported from: internal/fourslash/tests/outliningSpansForArguments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OutliningSpansForArgumentsCase
{
    public static void TestOutliningSpansForArguments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("console.log(123, 456)l;\nconsole.log(\n);\nconsole.log[|(\n    123, 456\n)|];\nconsole.log[|(\n    123,\n    456\n)|];\n() =>[| console.log[|(\n    123,\n    456\n)|]|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
