// ported from: internal/fourslash/tests/formatSelectionAfterTemplateLiteral1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSelectionAfterTemplateLiteral1Case
{
    public static void TestFormatSelectionAfterTemplateLiteral1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const a = `head${\"x\"};\n`;\n\n/*begin*/export const f = () => {\n    return `world`;\n/*end*/}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatSelection(t, "begin", "end");
                f.VerifyCurrentFileContent(t, "const a = `head${\"x\"};\n`;\n\nexport const f = () => {\n    return `world`;\n}\n");
            }
            finally
            {
                done();
            }
        });
    }

}
