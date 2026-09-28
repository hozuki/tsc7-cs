// ported from: internal/fourslash/tests/formattingVoid_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingVoidCase
{
    public static void TestFormattingVoid(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/  var x: () =>           void    ;\n/*2*/  var y:     void    ;\n/*3*/  function test(a:void,b:string){}\n/*4*/  var a, b, c, d;\n/*5*/  void    a    ;\n/*6*/  void        (0);\n/*7*/  b=void(c=1,d=2);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "var x: () => void;");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "var y: void;");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "function test(a: void, b: string) { }");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "void a;");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "void (0);");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "b = void (c = 1, d = 2);");
            }
            finally
            {
                done();
            }
        });
    }

}
