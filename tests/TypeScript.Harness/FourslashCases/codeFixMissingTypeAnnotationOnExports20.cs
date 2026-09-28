// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports20_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports20Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports20(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @lib: es2019\nexport function foo () {\n    return Symbol();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Add return type 'symbol'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'symbol'", NewFileContent = "export function foo (): symbol {\n    return Symbol();\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
