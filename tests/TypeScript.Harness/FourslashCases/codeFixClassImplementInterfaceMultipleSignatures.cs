// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMultipleSignatures_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMultipleSignaturesCase
{
    public static void TestCodeFixClassImplementInterfaceMultipleSignatures(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    method(a: number, b: string): boolean;\n    method(a: string, b: number): Function;\n    method(a: string): Function;\n}\n\nclass C implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "interface I {\n    method(a: number, b: string): boolean;\n    method(a: string, b: number): Function;\n    method(a: string): Function;\n}\n\nclass C implements I {\n    method(a: number, b: string): boolean;\n    method(a: string, b: number): Function;\n    method(a: string): Function;\n    method(a: unknown, b?: unknown): boolean | Function {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
