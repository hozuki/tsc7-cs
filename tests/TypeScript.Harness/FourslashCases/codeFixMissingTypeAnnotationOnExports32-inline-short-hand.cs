// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports32-inline-short-hand_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports32_inline_short_handCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports32_inline_short_hand(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /code.ts\nconst x = 1;\nexport default {\n  x\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add satisfies and an inline type assertion with 'number'", NewFileContent = "const x = 1;\nexport default {\n  x: x as number\n};", Index = 1 });
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add satisfies and an inline type assertion with 'typeof x'", NewFileContent = "const x = 1;\nexport default {\n  x: x as typeof x\n};", Index = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
