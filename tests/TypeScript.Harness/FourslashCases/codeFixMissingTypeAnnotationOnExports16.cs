// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports16_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports16Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports16(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nfunction foo() {\n    return { x: 1, y: {42: {dd: \"45\"}, b: 2} };\n}\nfunction foo3(): \"42\" {\n    return \"42\";\n}\nexport const { x: a , y: { [foo3()]: {dd: e} } } = foo();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract binding expressions to variable", NewFileContent = "function foo() {\n    return { x: 1, y: {42: {dd: \"45\"}, b: 2} };\n}\nfunction foo3(): \"42\" {\n    return \"42\";\n}\nconst dest = foo();\nexport const a: number = dest.x;\nconst _a = foo3();\nexport const e: string = (dest.y)[_a].dd;", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
