// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports_arrowParens_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports_arrowParensCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports_arrowParens(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nexport const func = x => x.substring(\"foo\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingTypeAnnotationOnExports", NewFileContent = "export const func = (x: any): any => x.substring(\"foo\");" });
            }
            finally
            {
                done();
            }
        });
    }

}
