// ported from: internal/fourslash/tests/codeFixClassImplementInterface_all_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterface_allCase
{
    public static void TestCodeFixClassImplementInterface_all(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I { i(): void; }\ninterface J { j(): void; }\nclass C implements I, J {}\nclass D implements J {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixClassIncorrectlyImplementsInterface", NewFileContent = "interface I { i(): void; }\ninterface J { j(): void; }\nclass C implements I, J {\n    i(): void {\n        throw new Error(\"Method not implemented.\");\n    }\n    j(): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}\nclass D implements J {\n    j(): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
