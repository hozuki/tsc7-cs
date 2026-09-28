// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports36-conditional-releative_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports36_conditional_releativeCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports36_conditional_releative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /code.ts\nconst A = \"A\"\nconst B = \"B\"\nexport const AB = Math.random()? A: B;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Add annotation of type '\"A\" | \"B\"'", "Add annotation of type 'typeof A | typeof B'", "Add annotation of type 'string'", "Add satisfies and an inline type assertion with '\"A\" | \"B\"'", "Add satisfies and an inline type assertion with 'typeof A | typeof B'", "Add satisfies and an inline type assertion with 'string'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add satisfies and an inline type assertion with 'typeof A | typeof B'", NewFileContent = "const A = \"A\"\nconst B = \"B\"\nexport const AB = (Math.random() ? A : B) satisfies typeof A | typeof B as typeof A | typeof B;", Index = 4 });
            }
            finally
            {
                done();
            }
        });
    }

}
