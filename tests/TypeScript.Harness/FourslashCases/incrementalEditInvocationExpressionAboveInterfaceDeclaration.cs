// ported from: internal/fourslash/tests/incrementalEditInvocationExpressionAboveInterfaceDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IncrementalEditInvocationExpressionAboveInterfaceDeclarationCase
{
    public static void TestIncrementalEditInvocationExpressionAboveInterfaceDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ndeclare function alert(message?: any): void;\n/*1*/\ninterface Foo {\n    setISO8601(dString): Date;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "alert(");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "alert(message?: any): void" });
                f.VerifyErrorExistsAfterMarker(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
