// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports50-generics-with-default_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports50_generics_with_defaultCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports50_generics_with_default(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2015\nlet x: Iterator<number>;\nexport const y = x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'Iterator<number>'", NewFileContent = "let x: Iterator<number>;\nexport const y: Iterator<number> = x;", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
