// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports9Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports9(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nfunction foo( ){\n    return 42;\n}\nconst a = foo();\nexport = a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'number'", NewFileContent = "function foo( ){\n    return 42;\n}\nconst a: number = foo();\nexport = a;", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
