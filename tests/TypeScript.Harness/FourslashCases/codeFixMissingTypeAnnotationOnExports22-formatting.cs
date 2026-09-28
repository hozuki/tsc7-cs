// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports22-formatting_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports22_formattingCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports22_formatting(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n/**\n * Test\n */\nexport function foo(){}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Add return type 'void'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'void'", NewFileContent = "/**\n * Test\n */\nexport function foo(): void{}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
