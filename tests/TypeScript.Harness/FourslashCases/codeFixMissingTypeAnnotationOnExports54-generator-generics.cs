// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports54-generator-generics_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports54_generator_genericsCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports54_generator_generics(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2015\nexport function foo(x: Generator<number>) {\n    return x;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'Generator<number>'", NewFileContent = "export function foo(x: Generator<number>): Generator<number> {\n    return x;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
