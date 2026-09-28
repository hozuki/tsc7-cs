// ported from: internal/fourslash/tests/formattingOnClosingBracket_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnClosingBracketCase
{
    public static void TestFormattingOnClosingBracket(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f( ) {/*1*/\nvar     x = 3;/*2*/\n    var z = 2   ;/*3*/\n    a  = z  ++ - 2 *  x ;/*4*/\n        for ( ; ; ) {/*5*/\n    a+=(g +g)*a%t;/*6*/\n        b --                          ;/*7*/\n}/*8*/\n\n    switch ( a  )/*9*/\n    {\n        case 1  :     {/*10*/\n    a ++  ;/*11*/\n        b--;/*12*/\n    if(a===a)/*13*/\n                return;/*14*/\n    else/*15*/\n        {\n            for(a in b)/*16*/\n                if(a!=a)/*17*/\n    {\n    for(a in b)/*18*/\n            {\na++;/*19*/\n        }/*20*/\n                }/*21*/\n    }/*22*/\n        }/*23*/\n    default:/*24*/\n        break;/*25*/\n    }/*26*/\n}/*27*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts874 = f.GetOptions();
                opts874!.FormatCodeSettings!.InsertSpaceAfterSemicolonInForStatements = TRI.TSTrue;
                f.Configure(t, opts874);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "function f() {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    var x = 3;");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    var z = 2;");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    a = z++ - 2 * x;");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    for (; ;) {");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "        a += (g + g) * a % t;");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "        b--;");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "    switch (a) {");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "        case 1: {");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "            a++;");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "            b--;");
                f.GoToMarker(t, "13");
                f.VerifyCurrentLineContent(t, "            if (a === a)");
                f.GoToMarker(t, "14");
                f.VerifyCurrentLineContent(t, "                return;");
                f.GoToMarker(t, "15");
                f.VerifyCurrentLineContent(t, "            else {");
                f.GoToMarker(t, "16");
                f.VerifyCurrentLineContent(t, "                for (a in b)");
                f.GoToMarker(t, "17");
                f.VerifyCurrentLineContent(t, "                    if (a != a) {");
                f.GoToMarker(t, "18");
                f.VerifyCurrentLineContent(t, "                        for (a in b) {");
                f.GoToMarker(t, "19");
                f.VerifyCurrentLineContent(t, "                            a++;");
                f.GoToMarker(t, "20");
                f.VerifyCurrentLineContent(t, "                        }");
                f.GoToMarker(t, "21");
                f.VerifyCurrentLineContent(t, "                    }");
                f.GoToMarker(t, "22");
                f.VerifyCurrentLineContent(t, "            }");
                f.GoToMarker(t, "23");
                f.VerifyCurrentLineContent(t, "        }");
                f.GoToMarker(t, "24");
                f.VerifyCurrentLineContent(t, "        default:");
                f.GoToMarker(t, "25");
                f.VerifyCurrentLineContent(t, "            break;");
                f.GoToMarker(t, "26");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "27");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
