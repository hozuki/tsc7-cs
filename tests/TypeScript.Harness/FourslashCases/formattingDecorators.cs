// ported from: internal/fourslash/tests/formattingDecorators_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingDecoratorsCase
{
    public static void TestFormattingDecorators(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/        @    decorator1    \n/*2*/            @        decorator2\n/*3*/    @decorator3\n/*4*/        @    decorator4    @            decorator5\n/*5*/class C {\n/*6*/            @    decorator6    \n/*7*/                @        decorator7\n/*8*/        @decorator8\n/*9*/    method1() { }\n\n/*10*/        @    decorator9    @            decorator10 @decorator11            method2() { }\n\n    method3(\n/*11*/                @    decorator12    \n/*12*/                    @        decorator13\n/*13*/            @decorator14\n/*14*/        x) { }\n\n    method4(\n/*15*/            @    decorator15    @            decorator16 @decorator17             x) { }\n\n/*16*/            @    decorator18    \n/*17*/                @        decorator19\n/*18*/        @decorator20    \n/*19*/    [\"computed1\"]() { }\n\n/*20*/        @    decorator21    @            decorator22 @decorator23            [\"computed2\"]() { }\n\n/*21*/            @    decorator24    \n/*22*/                @        decorator25\n/*23*/        @decorator26\n/*24*/    get accessor1() { }\n\n/*25*/        @    decorator27    @            decorator28 @decorator29            get accessor2() { }\n\n/*26*/            @    decorator30    \n/*27*/                @        decorator31\n/*28*/        @decorator32\n/*29*/    property1;\n\n/*30*/        @    decorator33    @            decorator34 @decorator35            property2;\n/*31*/function test(@decorator36@decorator37 param) {};\n/*32*/function test2(@decorator38()@decorator39()param) {};\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "@decorator1");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "@decorator2");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "@decorator3");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "@decorator4 @decorator5");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "class C {");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    @decorator6");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "    @decorator7");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    @decorator8");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "    method1() { }");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "    @decorator9 @decorator10 @decorator11 method2() { }");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "        @decorator12");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "        @decorator13");
                f.GoToMarker(t, "13");
                f.VerifyCurrentLineContent(t, "        @decorator14");
                f.GoToMarker(t, "14");
                f.VerifyCurrentLineContent(t, "        x) { }");
                f.GoToMarker(t, "15");
                f.VerifyCurrentLineContent(t, "        @decorator15 @decorator16 @decorator17 x) { }");
                f.GoToMarker(t, "16");
                f.VerifyCurrentLineContent(t, "    @decorator18");
                f.GoToMarker(t, "17");
                f.VerifyCurrentLineContent(t, "    @decorator19");
                f.GoToMarker(t, "18");
                f.VerifyCurrentLineContent(t, "    @decorator20");
                f.GoToMarker(t, "19");
                f.VerifyCurrentLineContent(t, "    [\"computed1\"]() { }");
                f.GoToMarker(t, "20");
                f.VerifyCurrentLineContent(t, "    @decorator21 @decorator22 @decorator23 [\"computed2\"]() { }");
                f.GoToMarker(t, "21");
                f.VerifyCurrentLineContent(t, "    @decorator24");
                f.GoToMarker(t, "22");
                f.VerifyCurrentLineContent(t, "    @decorator25");
                f.GoToMarker(t, "23");
                f.VerifyCurrentLineContent(t, "    @decorator26");
                f.GoToMarker(t, "24");
                f.VerifyCurrentLineContent(t, "    get accessor1() { }");
                f.GoToMarker(t, "25");
                f.VerifyCurrentLineContent(t, "    @decorator27 @decorator28 @decorator29 get accessor2() { }");
                f.GoToMarker(t, "26");
                f.VerifyCurrentLineContent(t, "    @decorator30");
                f.GoToMarker(t, "27");
                f.VerifyCurrentLineContent(t, "    @decorator31");
                f.GoToMarker(t, "28");
                f.VerifyCurrentLineContent(t, "    @decorator32");
                f.GoToMarker(t, "29");
                f.VerifyCurrentLineContent(t, "    property1;");
                f.GoToMarker(t, "30");
                f.VerifyCurrentLineContent(t, "    @decorator33 @decorator34 @decorator35 property2;");
                f.GoToMarker(t, "31");
                f.VerifyCurrentLineContent(t, "function test(@decorator36 @decorator37 param) { };");
                f.GoToMarker(t, "32");
                f.VerifyCurrentLineContent(t, "function test2(@decorator38() @decorator39() param) { };");
            }
            finally
            {
                done();
            }
        });
    }

}
