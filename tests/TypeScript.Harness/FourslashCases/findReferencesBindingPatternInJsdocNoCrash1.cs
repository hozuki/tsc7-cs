// ported from: internal/fourslash/tests/findReferencesBindingPatternInJsdocNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindReferencesBindingPatternInJsdocNoCrash1Case
{
    public static void TestFindReferencesBindingPatternInJsdocNoCrash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: node_modules/use-query/package.json\n{\n  \"name\": \"use-query\",\n  \"types\": \"index.d.ts\"\n}\n// @Filename: node_modules/use-query/index.d.ts\ndeclare function useQuery(): {\n  data: string[];\n};\n// @Filename: node_modules/other/package.json\n{\n  \"name\": \"other\",\n  \"types\": \"index.d.ts\"\n}\n// @Filename: node_modules/other/index.d.ts\ninterface BottomSheetModalProps {\n  /**\n   * A scrollable node or normal view.\n   * @type {({ data: any }?) => any}\n   */\n  children: ({ data: any }?) => any;\n}\n// @Filename: src/index.ts\nimport { useQuery } from \"use-query\";\nconst { /*1*/data } = useQuery();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
