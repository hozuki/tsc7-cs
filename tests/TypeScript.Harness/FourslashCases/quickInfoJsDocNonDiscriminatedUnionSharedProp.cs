// ported from: internal/fourslash/tests/quickInfoJsDocNonDiscriminatedUnionSharedProp_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocNonDiscriminatedUnionSharedPropCase
{
    public static void TestQuickInfoJsDocNonDiscriminatedUnionSharedProp(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface Entries {\n  /**\n   * Plugins info...\n   */\n  plugins?: Record<string, Record<string, unknown>>;\n  /**\n   * Output info...\n   */\n  output?: string;\n  /**\n   * Format info...\n   */\n  format?: string;\n}\n\ninterface Input extends Entries {\n  /**\n   * Input info...\n   */\n  input: string;\n}\n\ninterface Types extends Entries {\n  /**\n   * Types info...\n   */\n  types: string;\n}\n\ntype EntriesOptions = Input | Types;\n\nconst options: EntriesOptions[] = [\n  {\n    input: \"./src/index.ts\",\n    /*1*/output: \"./dist/index.mjs\",\n  },\n  {\n    types: \"./src/types.ts\",\n    format: \"esm\",\n  },\n];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) Entries.output?: string", "Output info...");
            }
            finally
            {
                done();
            }
        });
    }

}
