// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports57-generics-doesnt-drop-trailing-unknown_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports57_generics_doesnt_drop_trailing_unknownCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports57_generics_doesnt_drop_trailing_unknown(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2015\n\nlet x: unknown;\nexport const s = new Set([x]);\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'Set<unknown>'", NewFileContent = "\nlet x: unknown;\nexport const s: Set<unknown> = new Set([x]);\n", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
