// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports15_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports15Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports15(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @stableTypeOrdering: true\n// @isolatedDeclarations: true\n// @declaration: true\nfunction foo() {\n    return { x: 1, y: 1 } as const;\n}\nexport const { x, y = 0 } = foo();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract binding expressions to variable", NewFileContent = "function foo() {\n    return { x: 1, y: 1 } as const;\n}\nconst dest = foo();\nexport const x: 1 = dest.x;\nconst temp = dest.y;\nexport const y: 0 | 1 = temp === undefined ? 0 : dest.y;", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
