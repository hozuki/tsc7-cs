// ported from: internal/fourslash/tests/formattingInDestructuring3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingInDestructuring3Case
{
    public static void TestFormattingInDestructuring3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/const {\n/*2*/    a,\n/*3*/    b,\n/*4*/} = {a: 1, b: 2};\n/*5*/const {a: c} = {a: 1, b: 2};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "const {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    a,");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    b,");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "} = { a: 1, b: 2 };");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "const { a: c } = { a: 1, b: 2 };");
            }
            finally
            {
                done();
            }
        });
    }

}
