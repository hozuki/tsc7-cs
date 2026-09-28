// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports33-methods_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports33_methodsCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports33_methods(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /code.ts\nexport class Foo {\n  m() {\n  }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Add return type 'void'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'void'", NewFileContent = "export class Foo {\n  m(): void {\n  }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
