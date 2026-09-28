// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports29-inline_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports29_inlineCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports29_inline(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nfunction getString() {\n    return \"\"\n}\nexport const exp = {\n    prop: getString()\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add satisfies and an inline type assertion with 'string'", NewFileContent = "function getString() {\n    return \"\"\n}\nexport const exp = {\n    prop: getString() satisfies string as string\n};", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
