// ported from: internal/fourslash/tests/formatMultipleFunctionArguments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatMultipleFunctionArgumentsCase
{
    public static void TestFormatMultipleFunctionArguments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n someRandomFunction({\n   prop1: 1,\n   prop2: 2\n }, {\n   prop3: 3,\n   prop4: 4\n }, {\n   prop5: 5,\n   prop6: 6\n });\n\n someRandomFunction(\n     { prop7: 1, prop8: 2 },\n     { prop9: 3, prop10: 4 },\n     {\n       prop11: 5,\n       prop2: 6\n     }\n );"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nsomeRandomFunction({\n    prop1: 1,\n    prop2: 2\n}, {\n    prop3: 3,\n    prop4: 4\n}, {\n    prop5: 5,\n    prop6: 6\n});\n\nsomeRandomFunction(\n    { prop7: 1, prop8: 2 },\n    { prop9: 3, prop10: 4 },\n    {\n        prop11: 5,\n        prop2: 6\n    }\n);");
            }
            finally
            {
                done();
            }
        });
    }

}
