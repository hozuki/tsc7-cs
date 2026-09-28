// ported from: internal/fourslash/tests/formatDebuggerStatement_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatDebuggerStatementCase
{
    public static void TestFormatDebuggerStatement(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("if(false){debugger;}\n  if    (   false   )   {    debugger  ;   }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToBOF(t);
                f.VerifyCurrentLineContent(t, "if (false) { debugger; }");
                f.GoToEOF(t);
                f.VerifyCurrentLineContent(t, "if (false) { debugger; }");
            }
            finally
            {
                done();
            }
        });
    }

}
