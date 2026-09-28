// ported from: internal/fourslash/tests/codeFixInferFromFunctionThisUsageObjectPropertyParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixInferFromFunctionThisUsageObjectPropertyParameterCase
{
    public static void TestCodeFixInferFromFunctionThisUsageObjectPropertyParameter(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitThis: true\nfunction returnThisMember([| |]suffix: string) {\n     return this.member + suffix;\n }\n\n interface Container {\n     member: string;\n     returnThisMember(suffix: string): string;\n }\n\n const container: Container = {\n     member: \"sample\",\n     returnThisMember: returnThisMember,\n };\n\n container.returnThisMember(\"\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "this: Container, ", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
