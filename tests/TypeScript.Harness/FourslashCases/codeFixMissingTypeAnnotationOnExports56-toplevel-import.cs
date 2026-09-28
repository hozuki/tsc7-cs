// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports56-toplevel-import_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports56_toplevel_importCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports56_toplevel_import(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /person-code.ts\nexport interface Person { x: string; }\nexport function getPerson() : Person {\n  return null!\n}\n// @Filename: /code.ts\nimport { getPerson } from \"./person-code\";\nexport function wrapPerson() {\n  return { person: getPerson() }\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/code.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type '{ person: Person; }'", NewFileContent = "import { getPerson, Person } from \"./person-code\";\nexport function wrapPerson(): {\n    person: Person;\n} {\n  return { person: getPerson() }\n};", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
