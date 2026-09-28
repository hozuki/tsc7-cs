// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports37-array-spread_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports37_array_spreadCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports37_array_spread(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /code.ts\nconst Start = [\n  'A',\n  'B',\n] as const;\n\nconst End = [\n  \"Y\",\n  \"Z\"\n] as const;\nexport const All_Part1 = {};\nfunction getPart() {\n  return [\"Z\"]\n}\n\nexport const All = [\n  1,\n  ...Start,\n  1,\n  ...getPart(),\n  ...End,\n  1,\n] as const;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type '[...typeof All_Part1_1, ...typeof Start, ...typeof All_Part3, ...typeof All_Part4, ...typeof End, ...typeof All_Part6]'", NewFileContent = "const Start = [\n  'A',\n  'B',\n] as const;\n\nconst End = [\n  \"Y\",\n  \"Z\"\n] as const;\nexport const All_Part1 = {};\nfunction getPart() {\n  return [\"Z\"]\n}\n\nconst All_Part1_1 = [\n    1\n] as const;\nconst All_Part3 = [\n    1\n] as const;\nconst All_Part4 = getPart() as const;\nconst All_Part6 = [\n    1\n] as const;\nexport const All: [\n    ...typeof All_Part1_1,\n    ...typeof Start,\n    ...typeof All_Part3,\n    ...typeof All_Part4,\n    ...typeof End,\n    ...typeof All_Part6\n] = [\n    ...All_Part1_1,\n    ...Start,\n    ...All_Part3,\n    ...All_Part4,\n    ...End,\n    ...All_Part6\n] as const;", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
