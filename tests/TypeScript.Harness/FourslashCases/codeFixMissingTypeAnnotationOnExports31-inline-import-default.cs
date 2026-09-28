// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports31-inline-import-default_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports31_inline_import_defaultCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports31_inline_import_default(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /person-code.ts\nexport type Person = { x: string; }\nexport function getPerson() : Person {\n  return null!\n}\n// @Filename: /code.ts\nimport { getPerson } from \"./person-code\";\nexport default {\n  person: getPerson()\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/code.ts");
                f.VerifyCodeFixAvailable(t, ["Extract default export to variable", "Add satisfies and an inline type assertion with 'Person'", "Extract to variable and replace with 'newLocal as typeof newLocal'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add satisfies and an inline type assertion with 'Person'", NewFileContent = "import { getPerson, Person } from \"./person-code\";\nexport default {\n  person: getPerson() satisfies Person as Person\n};", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
