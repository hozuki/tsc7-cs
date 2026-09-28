// ported from: internal/fourslash/tests/quickInfoAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoAliasCase
{
    public static void TestQuickInfoAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n/**\n * Doc\n * @tag Tag text\n */\nexport const x = 0;\n// @Filename: /b.ts\nimport { x } from \"./a\";\nx/*b*/;\n// @Filename: /c.ts\n/**\n * Doc 2\n * @tag Tag text 2\n */\nimport {\n    /**\n     * Doc 3\n     * @tag Tag text 3\n     */\n    x\n} from \"./a\";\nx/*c*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
