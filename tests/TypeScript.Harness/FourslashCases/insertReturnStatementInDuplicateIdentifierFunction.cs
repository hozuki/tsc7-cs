// ported from: internal/fourslash/tests/insertReturnStatementInDuplicateIdentifierFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InsertReturnStatementInDuplicateIdentifierFunctionCase
{
    public static void TestInsertReturnStatementInDuplicateIdentifierFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\nclass foo { };\nfunction foo() { /**/ }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyNumberOfErrorsInCurrentFile(t, 2);
                f.Insert(t, "return null;");
                f.VerifyNumberOfErrorsInCurrentFile(t, 2);
            }
            finally
            {
                done();
            }
        });
    }

}
