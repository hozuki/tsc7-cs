// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports40-extract-other-to-variable_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports40_extract_other_to_variableCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports40_extract_other_to_variable(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nlet c: string[] = [];\nexport let o = {\n    p: Math.random() ? []: [\n        ...c\n    ]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract to variable and replace with 'newLocal as typeof newLocal'", NewFileContent = "let c: string[] = [];\nconst newLocal = Math.random() ? [] : [\n    ...c\n];\nexport let o = {\n    p: newLocal as typeof newLocal\n}", Index = 2, ApplyChanges = true });
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'string[]'", NewFileContent = "let c: string[] = [];\nconst newLocal: string[] = Math.random() ? [] : [\n    ...c\n];\nexport let o = {\n    p: newLocal as typeof newLocal\n}", Index = 0, ApplyChanges = true });
            }
            finally
            {
                done();
            }
        });
    }

}
