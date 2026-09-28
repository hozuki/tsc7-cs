// ported from: internal/fourslash/tests/formattingOnObjectLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnObjectLiteralCase
{
    public static void TestFormattingOnObjectLiteral(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = /*1*/{foo:/*2*/ 1,\nbar: \"tt\",/*3*/\nboo: /*4*/1 + 5}/*5*/;\n\nvar x2 = /*6*/{foo/*7*/: 1,\nbar: /*8*/\"tt\",boo:1+5}/*9*/;\n\nfunction Foo() {/*10*/\nvar typeICalc = {/*11*/\nclear: {/*12*/\n\"()\": [1, 2, 3]/*13*/\n}/*14*/\n}/*15*/\n}/*16*/\n\n// Rule for object literal members for the \"value\" of the memebr to follow the indent/*17*/\n// of the member, i.e. the relative position of the value is maintained when the member/*18*/\n// is indented./*19*/\nvar x2 = {/*20*/\n  foo:/*21*/\n3,/*22*/\n          'bar':/*23*/\n                    { a: 1, b : 2}/*24*/\n};/*25*/\n\nvar x={    };/*26*/\nvar y = {};/*27*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "var x = {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    foo: 1,");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    bar: \"tt\",");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    boo: 1 + 5");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "};");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "var x2 = {");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "    foo: 1,");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    bar: \"tt\", boo: 1 + 5");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "};");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "function Foo() {");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "    var typeICalc = {");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "        clear: {");
                f.GoToMarker(t, "13");
                f.VerifyCurrentLineContent(t, "            \"()\": [1, 2, 3]");
                f.GoToMarker(t, "14");
                f.VerifyCurrentLineContent(t, "        }");
                f.GoToMarker(t, "15");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "16");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "17");
                f.VerifyCurrentLineContent(t, "// Rule for object literal members for the \"value\" of the memebr to follow the indent");
                f.GoToMarker(t, "18");
                f.VerifyCurrentLineContent(t, "// of the member, i.e. the relative position of the value is maintained when the member");
                f.GoToMarker(t, "19");
                f.VerifyCurrentLineContent(t, "// is indented.");
                f.GoToMarker(t, "20");
                f.VerifyCurrentLineContent(t, "var x2 = {");
                f.GoToMarker(t, "21");
                f.VerifyCurrentLineContent(t, "    foo:");
                f.GoToMarker(t, "22");
                f.VerifyCurrentLineContent(t, "        3,");
                f.GoToMarker(t, "23");
                f.VerifyCurrentLineContent(t, "    'bar':");
                f.GoToMarker(t, "24");
                f.VerifyCurrentLineContent(t, "        { a: 1, b: 2 }");
                f.GoToMarker(t, "25");
                f.VerifyCurrentLineContent(t, "};");
                f.GoToMarker(t, "26");
                f.VerifyCurrentLineContent(t, "var x = {};");
                f.GoToMarker(t, "27");
                f.VerifyCurrentLineContent(t, "var y = {};");
            }
            finally
            {
                done();
            }
        });
    }

}
