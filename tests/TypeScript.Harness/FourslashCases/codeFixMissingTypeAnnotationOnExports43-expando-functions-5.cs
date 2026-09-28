// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports43-expando-functions-5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports43_expando_functions_5Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports43_expando_functions_5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nfunction foo(): void {}\n// x already exists, so do not generate code for 'x'\nfoo.x = 1;\nfoo.y = 1;\nnamespace foo {\n  export let x = 42;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Annotate types of properties expando function in a namespace", NewFileContent = "function foo(): void {}\ndeclare namespace foo {\n    export var y: number;\n}\n// x already exists, so do not generate code for 'x'\nfoo.x = 1;\nfoo.y = 1;\nnamespace foo {\n  export let x = 42;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
