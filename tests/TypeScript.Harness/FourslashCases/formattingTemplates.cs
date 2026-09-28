// ported from: internal/fourslash/tests/formattingTemplates_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingTemplatesCase
{
    public static void TestFormattingTemplates(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("String.call `${123}`/*1*/\nString.call `${123} ${456}`/*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, ";");
                f.VerifyCurrentLineContent(t, "String.call`${123}`;");
                f.GoToMarker(t, "2");
                f.Insert(t, ";");
                f.VerifyCurrentLineContent(t, "String.call`${123} ${456}`;");
            }
            finally
            {
                done();
            }
        });
    }

}
