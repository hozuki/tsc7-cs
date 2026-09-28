// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports14_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports14Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports14(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nfunction foo() {\n    return { x: 1, y: 1};\n}\nexport const { x, y = 0} = foo(), z= 42;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract binding expressions to variable", NewFileContent = "function foo() {\n    return { x: 1, y: 1};\n}\nconst dest = foo();\nexport const x: number = dest.x;\nconst temp = dest.y;\nexport const y: number = temp === undefined ? 0 : dest.y;\nexport const z = 42;", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
