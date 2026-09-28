// ported from: internal/fourslash/tests/codeFixClassImplementClassMultipleSignatures1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementClassMultipleSignatures1Case
{
    public static void TestCodeFixClassImplementClassMultipleSignatures1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    method(a: number, b: string): boolean;\n    method(a: string | number, b?: string | number): boolean | Function { return a + b as any; }\n}\nclass C implements A {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'A'", NewFileContent = "class A {\n    method(a: number, b: string): boolean;\n    method(a: string | number, b?: string | number): boolean | Function { return a + b as any; }\n}\nclass C implements A {\n    method(a: number, b: string): boolean;\n    method(a: string | number, b?: string | number): boolean | Function {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
