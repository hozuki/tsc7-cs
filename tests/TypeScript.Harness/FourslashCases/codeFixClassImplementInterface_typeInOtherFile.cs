// ported from: internal/fourslash/tests/codeFixClassImplementInterface_typeInOtherFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterface_typeInOtherFileCase
{
    public static void TestCodeFixClassImplementInterface_typeInOtherFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /I.ts\nexport interface J {}\nexport interface I {\n    x: J;\n    m(): J;\n}\n// @Filename: /C.ts\nimport { I } from \"./I\";\nexport class C implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/C.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "import { I, J } from \"./I\";\nexport class C implements I {\n    x: J;\n    m(): J {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
