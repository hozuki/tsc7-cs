// ported from: internal/fourslash/tests/formattingInDestructuring4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingInDestructuring4Case
{
    public static void TestFormattingInDestructuring4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/const { \n/*2*/    a,\n/*3*/    b,\n/*4*/} = { a: 1, b: 2 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts198 = f.GetOptions();
                opts198!.FormatCodeSettings!.InsertSpaceAfterOpeningAndBeforeClosingNonemptyBraces = TRI.TSFalse;
                f.Configure(t, opts198);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "const {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    a,");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    b,");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "} = {a: 1, b: 2};");
            }
            finally
            {
                done();
            }
        });
    }

}
