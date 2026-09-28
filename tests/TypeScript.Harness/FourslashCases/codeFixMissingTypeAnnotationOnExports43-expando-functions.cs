// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports43-expando-functions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports43_expando_functionsCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports43_expando_functions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nconst foo = (): void => {}\nfoo.a = \"A\";\nfoo.b = \"C\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type '{ (): void; a: string; b: string; }'", NewFileContent = "const foo: {\n    (): void;\n    a: string;\n    b: string;\n} = (): void => {}\nfoo.a = \"A\";\nfoo.b = \"C\"", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
