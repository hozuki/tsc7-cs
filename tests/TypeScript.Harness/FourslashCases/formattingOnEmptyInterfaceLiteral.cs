// ported from: internal/fourslash/tests/formattingOnEmptyInterfaceLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnEmptyInterfaceLiteralCase
{
    public static void TestFormattingOnEmptyInterfaceLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/    function    foo  (  x  :    {    }    )    {    }\n\n/*2*/foo    (  {     }   )    ;\n\n\n\n/*3*/            interface    bar    {\n/*4*/                x   :    {     }   ;\n/*5*/       y  :       (         )    =>    {     }   ;\n/*6*/                                                    }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "function foo(x: {}) { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "foo({});");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "interface bar {");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    x: {};");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    y: () => {};");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
