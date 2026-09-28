// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports38-unique-symbol-return_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports38_unique_symbol_returnCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports38_unique_symbol_return(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nconst u: unique symbol = Symbol();\nexport const fn = () => ({ u } as const);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type '{ readonly u: typeof u; }'", NewFileContent = "const u: unique symbol = Symbol();\nexport const fn = (): {\n    readonly u: typeof u;\n} => ({ u } as const);", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
