// ported from: internal/fourslash/tests/quickInfoDisplayPartsTypeParameterInClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsTypeParameterInClassCase
{
    public static void TestQuickInfoDisplayPartsTypeParameterInClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class /*1*/c</*2*/T> {\n    /*3*/constructor(/*4*/a: /*5*/T) {\n    }\n    /*6*/method</*7*/U>(/*8*/a: /*9*/U, /*10*/b: /*11*/T) {\n        return /*12*/a;\n    }\n}\nvar /*13*/cInstance = new /*14*/c(\"Hello\");\nvar /*15*/cVal = /*16*/c;\n/*17*/cInstance./*18*/method(\"hello\", \"cello\");\nclass /*19*/c2</*20*/T extends /*21*/c<string>> {\n    /*22*/constructor(/*23*/a: /*24*/T) {\n    }\n    /*25*/method</*26*/U extends /*27*/c<string>>(/*28*/a: /*29*/U, /*30*/b: /*31*/T) {\n        return /*32*/a;\n    }\n}\nvar /*33*/cInstance1 = new /*34*/c2(/*35*/cInstance);\nvar /*36*/cVal2 = /*37*/c2;\n/*38*/cInstance1./*39*/method(/*40*/cInstance, /*41*/cInstance);"u8);
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
