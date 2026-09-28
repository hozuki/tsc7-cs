// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports49-private-name_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports49_private_nameCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports49_private_name(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @moduleResolution: bundler\n// @target: es2018\n// @jsx: react-jsx\nexport function two() {\n    const y = \"\";\n    return {} as typeof y;\n}\n\nexport function three() {\n    type Z = string;\n    return {} as Z;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type '\"\"'", NewFileContent = "export function two(): \"\" {\n    const y = \"\";\n    return {} as typeof y;\n}\n\nexport function three() {\n    type Z = string;\n    return {} as Z;\n}", Index = 0 });
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'string'", NewFileContent = "export function two() {\n    const y = \"\";\n    return {} as typeof y;\n}\n\nexport function three(): string {\n    type Z = string;\n    return {} as Z;\n}", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
