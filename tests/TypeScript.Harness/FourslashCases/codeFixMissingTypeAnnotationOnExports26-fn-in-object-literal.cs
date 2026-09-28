// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports26-fn-in-object-literal_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports26_fn_in_object_literalCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports26_fn_in_object_literal(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nexport const extensions = {\n    /**\n     */\n    fn: <T>(actualValue: T, expectedValue: T) => {\n       return actualValue === expectedValue\n    },\n    fn2: function<T>(actualValue: T, expectedValue: T)  {\n       return actualValue === expectedValue\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingTypeAnnotationOnExports", NewFileContent = "export const extensions = {\n    /**\n     */\n    fn: <T>(actualValue: T, expectedValue: T): boolean => {\n       return actualValue === expectedValue\n    },\n    fn2: function<T>(actualValue: T, expectedValue: T): boolean  {\n       return actualValue === expectedValue\n    }\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
