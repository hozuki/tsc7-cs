// ported from: internal/fourslash/tests/typeAssertionsFormatting_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TypeAssertionsFormattingCase
{
    public static void TestTypeAssertionsFormatting(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("( <  any   >      publisher);/*1*/\n <  any  >      3;/*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "(<any>publisher);");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "<any>3;");
            }
            finally
            {
                done();
            }
        });
    }

}
