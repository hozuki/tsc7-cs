// ported from: internal/fourslash/tests/quickInfoDisplayPartsTypeParameterInInterface_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsTypeParameterInInterfaceCase
{
    public static void TestQuickInfoDisplayPartsTypeParameterInInterface(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface /*1*/I</*2*/T> {\n    new </*3*/U>(/*4*/a: /*5*/U, /*6*/b: /*7*/T): /*8*/U;\n    </*9*/U>(/*10*/a: /*11*/U, /*12*/b: /*13*/T): /*14*/U;\n    /*15*/method</*16*/U>(/*17*/a: /*18*/U, /*19*/b: /*20*/T): /*21*/U;\n}\nvar /*22*/iVal: /*23*/I<string>;\nnew /*24*/iVal(\"hello\", \"hello\");\n/*25*/iVal(\"hello\", \"hello\");\n/*26*/iVal./*27*/method(\"hello\", \"hello\");\ninterface /*28*/I1</*29*/T extends /*30*/I<string>> {\n    new </*31*/U extends /*32*/I<string>>(/*33*/a: /*34*/U, /*35*/b: /*36*/T): /*37*/U;\n    </*38*/U extends /*39*/I<string>>(/*40*/a: /*41*/U, /*42*/b: /*43*/T): /*44*/U;\n    /*45*/method</*46*/U extends /*47*/I<string>>(/*48*/a: /*49*/U, /*50*/b: /*51*/T): /*52*/U;\n}\nvar /*53*/iVal1: /*54*/I1</*55*/I<string>>;\nnew /*56*/iVal1(/*57*/iVal, /*58*/iVal);\n/*59*/iVal1(/*60*/iVal, /*61*/iVal);\n/*62*/iVal1./*63*/method(/*64*/iVal, /*65*/iVal);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
