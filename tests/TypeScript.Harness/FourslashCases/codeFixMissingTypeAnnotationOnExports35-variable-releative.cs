// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports35-variable-releative_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports35_variable_releativeCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports35_variable_releative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /code.ts\nconst foo = { a: 1 }\nexport const exported = foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'typeof foo'", NewFileContent = "const foo = { a: 1 }\nexport const exported: typeof foo = foo;", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
