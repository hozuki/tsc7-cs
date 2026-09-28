// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports21-params-and-return_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports21_params_and_returnCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports21_params_and_return(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n/**\n * Test\n */\nexport function foo(): number { return 0; }\n/**\n* Docs\n*/\nexport const bar = (a = foo()) =>\n   a;\n// Trivia"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'number'", NewFileContent = "/**\n * Test\n */\nexport function foo(): number { return 0; }\n/**\n* Docs\n*/\nexport const bar = (a = foo()): number =>\n   a;\n// Trivia", Index = 0, ApplyChanges = true });
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'number'", NewFileContent = "/**\n * Test\n */\nexport function foo(): number { return 0; }\n/**\n* Docs\n*/\nexport const bar = (a: number = foo()): number =>\n   a;\n// Trivia", Index = 0, ApplyChanges = true });
            }
            finally
            {
                done();
            }
        });
    }

}
