// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports44-default-export_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports44_default_exportCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports44_default_export(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\n// @Filename: /code.ts\nexport default 1 + 1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract default export to variable", NewFileContent = "const _default_1: number = 1 + 1;\nexport default _default_1;", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
