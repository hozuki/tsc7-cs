// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports8Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nfunction foo() {return 42;}\nexport const g = function () { return foo(); };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Add return type 'number'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'number'", NewFileContent = "function foo() {return 42;}\nexport const g = function (): number { return foo(); };", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
