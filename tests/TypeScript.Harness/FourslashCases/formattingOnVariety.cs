// ported from: internal/fourslash/tests/formattingOnVariety_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnVarietyCase
{
    public static void TestFormattingOnVariety(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f(a,b,c,d){/*1*/\nfor(var i=0;i<10;i++){/*2*/\nvar a=0;/*3*/\nvar b=a+a+a*a%a/2-1;/*4*/\nb+=a;/*5*/\n++b;/*6*/\nf(a,b,c,d);/*7*/\nif(1===1){/*8*/\nvar m=function(e,f){/*9*/\nreturn e^f;/*10*/\n}/*11*/\n}/*12*/\n}/*13*/\n}/*14*/\n\nfor (var i = 0   ; i < this.foo(); i++) {/*15*/\n}/*16*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "function f(a, b, c, d) {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    for (var i = 0; i < 10; i++) {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "        var a = 0;");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "        var b = a + a + a * a % a / 2 - 1;");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "        b += a;");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "        ++b;");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "        f(a, b, c, d);");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "        if (1 === 1) {");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "            var m = function(e, f) {");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "                return e ^ f;");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "            }");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "        }");
                f.GoToMarker(t, "13");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "14");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "15");
                f.VerifyCurrentLineContent(t, "for (var i = 0; i < this.foo(); i++) {");
                f.GoToMarker(t, "16");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
