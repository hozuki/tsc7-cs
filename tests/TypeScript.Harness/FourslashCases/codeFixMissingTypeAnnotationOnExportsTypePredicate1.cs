// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExportsTypePredicate1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExportsTypePredicate1Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExportsTypePredicate1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @filename: index.ts\nexport function isString(value: unknown) {\n  return typeof value === \"string\";\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'value is string'", NewFileContent = "export function isString(value: unknown): value is string {\n  return typeof value === \"string\";\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
