// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports52-generics-oversimplification_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports52_generics_oversimplificationCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports52_generics_oversimplification(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nexport interface Foo<T, U = T[]> {}\nexport function foo(x: Foo<string, string[]>) {\n    return x;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'Foo<string>'", NewFileContent = "export interface Foo<T, U = T[]> {}\nexport function foo(x: Foo<string, string[]>): Foo<string> {\n    return x;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
