// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports43-expando-functions-2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports43_expando_functions_2Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports43_expando_functions_2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nconst foo = () => {}\nfoo/*a*/[\"a\"] = \"A\";\nfoo[\"b\"] = \"C\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type '{ (): void; a: string; b: string; }'", NewFileContent = "const foo: {\n    (): void;\n    a: string;\n    b: string;\n} = () => {}\nfoo[\"a\"] = \"A\";\nfoo[\"b\"] = \"C\"", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
