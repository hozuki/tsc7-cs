// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports39-extract-arr-to-variable_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports39_extract_arr_to_variableCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports39_extract_arr_to_variable(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nlet c: string[] = [];\nexport let o = {\n    p: [\n        ...c\n    ]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Mark array literal as const", NewFileContent = "let c: string[] = [];\nexport let o = {\n    p: [\n        ...c\n    ] as const\n}", Index = 2, ApplyChanges = true });
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract to variable and replace with 'newLocal as typeof newLocal'", NewFileContent = "let c: string[] = [];\nconst newLocal = [\n    ...c\n] as const;\nexport let o = {\n    p: newLocal as typeof newLocal\n}", Index = 1, ApplyChanges = true });
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'readonly string[]'", NewFileContent = "let c: string[] = [];\nconst newLocal: readonly string[] = [\n    ...c\n] as const;\nexport let o = {\n    p: newLocal as typeof newLocal\n}", Index = 0, ApplyChanges = true });
            }
            finally
            {
                done();
            }
        });
    }

}
