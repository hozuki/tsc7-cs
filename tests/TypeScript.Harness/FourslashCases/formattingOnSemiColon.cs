// ported from: internal/fourslash/tests/formattingOnSemiColon_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnSemiColonCase
{
    public static void TestFormattingOnSemiColon(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var  a=b+c^d-e*++f"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToEOF(t);
                f.Insert(t, ";");
                f.VerifyCurrentFileContent(t, "var a = b + c ^ d - e * ++f;");
            }
            finally
            {
                done();
            }
        });
    }

}
