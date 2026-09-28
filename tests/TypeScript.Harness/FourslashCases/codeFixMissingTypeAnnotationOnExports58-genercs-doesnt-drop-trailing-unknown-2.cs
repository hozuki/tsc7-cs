// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports58-genercs-doesnt-drop-trailing-unknown-2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports58_genercs_doesnt_drop_trailing_unknown_2Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports58_genercs_doesnt_drop_trailing_unknown_2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2015\n\nexport const s = new Set<unknown>();\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'Set<unknown>'", NewFileContent = "\nexport const s: Set<unknown> = new Set<unknown>();\n", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
