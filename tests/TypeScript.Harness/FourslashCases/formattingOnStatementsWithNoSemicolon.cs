// ported from: internal/fourslash/tests/formattingOnStatementsWithNoSemicolon_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnStatementsWithNoSemicolonCase
{
    public static void TestFormattingOnStatementsWithNoSemicolon(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/do\n     { var a/*2*/\n/*3*/}   while (1)\n/*4*/function f() {\n/*5*/    var s = 1\n/*6*/            }\n/*7*/switch (t) {\n/*8*/    case 1:\n/*9*/{\n/*10*/test\n/*11*/}\n/*12*/}\n/*13*/do{do{do{}while(a!==b)}while(a!==b)}while(a!==b)\n/*14*/do{\n/*15*/do{\n/*16*/do{\n/*17*/}while(a!==b)\n/*18*/}while(a!==b)\n/*19*/}while(a!==b)\n/*20*/for(var i=0;i<10;i++){\n/*21*/for(var j=0;j<10;j++){\n/*22*/j-=i\n/*23*/}/*24*/}\n/*25*/function foo() {\n/*26*/try {\n/*27*/x+=2\n/*28*/}\n/*29*/catch( e){\n/*30*/x+=2\n/*31*/}finally {\n/*32*/x+=2\n/*33*/}\n/*34*/}\n/*35*/do     { var a }   while (1)\n    foo(function (file) {/*49*/\n        return 0/*50*/\n    }).then(function (doc) {/*51*/\n        return 1/*52*/\n    });/*53*/\n/*54*/if(1)\n/*55*/if(1)\n/*56*/x++\n/*57*/else\n/*58*/if(1)\n/*59*/x+=2\n/*60*/else\n/*61*/x+=2\n\n\n\n/*62*/;\n         do do do do/*63*/\n                test;/*64*/\n            while (0)/*65*/\n         while (0)/*66*/\n            while (0)/*67*/\n         while (0)/*68*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "do {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    var a");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "} while (1)");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "function f() {");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    var s = 1");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "switch (t) {");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    case 1:");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "        {");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "            test");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "        }");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "13");
                f.VerifyCurrentLineContent(t, "do { do { do { } while (a !== b) } while (a !== b) } while (a !== b)");
                f.GoToMarker(t, "14");
                f.VerifyCurrentLineContent(t, "do {");
                f.GoToMarker(t, "15");
                f.VerifyCurrentLineContent(t, "    do {");
                f.GoToMarker(t, "16");
                f.VerifyCurrentLineContent(t, "        do {");
                f.GoToMarker(t, "17");
                f.VerifyCurrentLineContent(t, "        } while (a !== b)");
                f.GoToMarker(t, "18");
                f.VerifyCurrentLineContent(t, "    } while (a !== b)");
                f.GoToMarker(t, "19");
                f.VerifyCurrentLineContent(t, "} while (a !== b)");
                f.GoToMarker(t, "20");
                f.VerifyCurrentLineContent(t, "for (var i = 0; i < 10; i++) {");
                f.GoToMarker(t, "21");
                f.VerifyCurrentLineContent(t, "    for (var j = 0; j < 10; j++) {");
                f.GoToMarker(t, "22");
                f.VerifyCurrentLineContent(t, "        j -= i");
                f.GoToMarker(t, "23");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "24");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "25");
                f.VerifyCurrentLineContent(t, "function foo() {");
                f.GoToMarker(t, "26");
                f.VerifyCurrentLineContent(t, "    try {");
                f.GoToMarker(t, "27");
                f.VerifyCurrentLineContent(t, "        x += 2");
                f.GoToMarker(t, "28");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "29");
                f.VerifyCurrentLineContent(t, "    catch (e) {");
                f.GoToMarker(t, "30");
                f.VerifyCurrentLineContent(t, "        x += 2");
                f.GoToMarker(t, "31");
                f.VerifyCurrentLineContent(t, "    } finally {");
                f.GoToMarker(t, "32");
                f.VerifyCurrentLineContent(t, "        x += 2");
                f.GoToMarker(t, "33");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "34");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "35");
                f.VerifyCurrentLineContent(t, "do { var a } while (1)");
                f.GoToMarker(t, "49");
                f.VerifyCurrentLineContent(t, "foo(function(file) {");
                f.GoToMarker(t, "50");
                f.VerifyCurrentLineContent(t, "    return 0");
                f.GoToMarker(t, "51");
                f.VerifyCurrentLineContent(t, "}).then(function(doc) {");
                f.GoToMarker(t, "52");
                f.VerifyCurrentLineContent(t, "    return 1");
                f.GoToMarker(t, "53");
                f.VerifyCurrentLineContent(t, "});");
                f.GoToMarker(t, "54");
                f.VerifyCurrentLineContent(t, "if (1)");
                f.GoToMarker(t, "55");
                f.VerifyCurrentLineContent(t, "    if (1)");
                f.GoToMarker(t, "56");
                f.VerifyCurrentLineContent(t, "        x++");
                f.GoToMarker(t, "57");
                f.VerifyCurrentLineContent(t, "    else");
                f.GoToMarker(t, "58");
                f.VerifyCurrentLineContent(t, "        if (1)");
                f.GoToMarker(t, "59");
                f.VerifyCurrentLineContent(t, "            x += 2");
                f.GoToMarker(t, "60");
                f.VerifyCurrentLineContent(t, "        else");
                f.GoToMarker(t, "61");
                f.VerifyCurrentLineContent(t, "            x += 2");
                f.GoToMarker(t, "62");
                f.VerifyCurrentLineContent(t, "                ;");
                f.GoToMarker(t, "63");
                f.VerifyCurrentLineContent(t, "do do do do");
                f.GoToMarker(t, "64");
                f.VerifyCurrentLineContent(t, "    test;");
                f.GoToMarker(t, "65");
                f.VerifyCurrentLineContent(t, "while (0)");
                f.GoToMarker(t, "66");
                f.VerifyCurrentLineContent(t, "while (0)");
                f.GoToMarker(t, "67");
                f.VerifyCurrentLineContent(t, "while (0)");
                f.GoToMarker(t, "68");
                f.VerifyCurrentLineContent(t, "while (0)");
            }
            finally
            {
                done();
            }
        });
    }

}
