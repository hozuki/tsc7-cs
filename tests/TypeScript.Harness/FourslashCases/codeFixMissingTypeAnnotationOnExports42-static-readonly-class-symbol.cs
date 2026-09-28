// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports42-static-readonly-class-symbol_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports42_static_readonly_class_symbolCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports42_static_readonly_class_symbol(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nclass A {\n    static readonly p1 = Symbol();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'unique symbol'", NewFileContent = "class A {\n    static readonly p1: unique symbol = Symbol();\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
