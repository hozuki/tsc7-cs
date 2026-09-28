// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports4Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nconst a = 42;\nconst b = 42;\nexport class C {\n  method() { return a + b };\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Add return type 'number'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'number'", NewFileContent = "const a = 42;\nconst b = 42;\nexport class C {\n  method(): number { return a + b };\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
