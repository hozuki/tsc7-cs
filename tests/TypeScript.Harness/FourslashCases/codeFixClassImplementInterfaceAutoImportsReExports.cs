// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceAutoImportsReExports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceAutoImportsReExportsCase
{
    public static void TestCodeFixClassImplementInterfaceAutoImportsReExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: node_modules/test-module/index.d.ts\ndeclare namespace e {\n    interface Foo {}\n}\nexport = e;\n// @Filename: a.ts\nimport { Foo } from \"test-module\";\nexport interface A {\n    foo(): Foo;\n}\n// @Filename: b.ts\nimport { A } from \"./a\";\nexport class B implements A {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "b.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'A'", NewFileContent = "import { Foo } from \"test-module\";\nimport { A } from \"./a\";\nexport class B implements A {\n    foo(): Foo {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
