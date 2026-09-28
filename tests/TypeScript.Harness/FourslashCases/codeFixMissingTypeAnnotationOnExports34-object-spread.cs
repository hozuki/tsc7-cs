// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports34-object-spread_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports34_object_spreadCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports34_object_spread(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /code.ts\nconst Start = {\n  A: 'A',\n  B: 'B',\n} as const;\n\nconst End = {\n  Y: \"Y\",\n  Z: \"Z\"\n} as const;\nexport const All_Part1 = {};\nfunction getPart() {\n  return { M: \"Z\"}\n}\n\nexport const All = {\n  x: 1,\n  ...Start,\n  y: 1,\n  ...getPart(),\n  ...End,\n  z: 1,\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'typeof All_Part1_1 & typeof Start & typeof All_Part3 & typeof All_Part4 & typeof End & typeof All_Part6'", NewFileContent = "const Start = {\n  A: 'A',\n  B: 'B',\n} as const;\n\nconst End = {\n  Y: \"Y\",\n  Z: \"Z\"\n} as const;\nexport const All_Part1 = {};\nfunction getPart() {\n  return { M: \"Z\"}\n}\n\nconst All_Part1_1 = {\n    x: 1\n};\nconst All_Part3 = {\n    y: 1\n};\nconst All_Part4 = getPart();\nconst All_Part6 = {\n    z: 1\n};\nexport const All: typeof All_Part1_1 & typeof Start & typeof All_Part3 & typeof All_Part4 & typeof End & typeof All_Part6 = {\n    ...All_Part1_1,\n    ...Start,\n    ...All_Part3,\n    ...All_Part4,\n    ...End,\n    ...All_Part6\n};", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
