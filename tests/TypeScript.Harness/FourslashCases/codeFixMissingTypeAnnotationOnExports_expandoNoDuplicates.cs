// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports_expandoNoDuplicates_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports_expandoNoDuplicatesCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports_expandoNoDuplicates(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @declaration: true\n// @isolatedDeclarations: true\n// @Filename: /foo.mts\nexport function foo(): void {\n}\n\nfoo.blah = 123;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailableExact(t, ["Annotate types of properties expando function in a namespace"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Annotate types of properties expando function in a namespace", NewFileContent = "export function foo(): void {\n}\nexport declare namespace foo {\n    export var blah: number;\n}\n\nfoo.blah = 123;" });
            }
            finally
            {
                done();
            }
        });
    }

}
