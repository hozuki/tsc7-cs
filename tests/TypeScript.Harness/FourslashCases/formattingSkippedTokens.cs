// ported from: internal/fourslash/tests/formattingSkippedTokens_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingSkippedTokensCase
{
    public static void TestFormattingSkippedTokens(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/foo(): Bar { }\n/*2*/function Foo      () #   { }\n/*3*/4+:5\n namespace M {\nfunction a(\n/*4*/    : T) { }\n}\n/*5*/var x       ="u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "foo(): Bar { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "function Foo() #   { }");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "4 +: 5");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    : T) { }");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "var x =");
            }
            finally
            {
                done();
            }
        });
    }

}
