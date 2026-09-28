// ported from: internal/fourslash/tests/formatVariableDeclarationList_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatVariableDeclarationListCase
{
    public static void TestFormatVariableDeclarationList(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/var   fun1   =   function   (     )     {\n/*2*/            var               x   =   'foo'             ,\n/*3*/                z   =   'bar'           ;\n/*4*/                return  x            ;\n/*5*/},\n\n/*6*/fun2   =   (                function        (   f               )   {\n/*7*/            var   fun   =   function   (        )       {\n/*8*/                        console         .  log             (           f     (  )  )       ;\n/*9*/            },\n/*10*/            x   =   'Foo'           ;\n/*11*/                return   fun            ;\n/*12*/}   (           fun1            )   )       ;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "var fun1 = function() {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    var x = 'foo',");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "        z = 'bar';");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    return x;");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "},");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    fun2 = (function(f) {");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "        var fun = function() {");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "            console.log(f());");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "        },");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "            x = 'Foo';");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "        return fun;");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "    }(fun1));");
            }
            finally
            {
                done();
            }
        });
    }

}
