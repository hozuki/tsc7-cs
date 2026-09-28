// ported from: internal/fourslash/tests/quickInfoJsDocInheritage_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocInheritageCase
{
    public static void TestQuickInfoJsDocInheritage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A {\n    /**\n     * @description A.foo1\n     */\n    foo1: number;\n    /**\n     * @description A.foo2\n     */\n    foo2: (para1: string) => number;\n}\n\ninterface B {\n    /**\n     * @description B.foo1\n     */\n    foo1: number;\n    /**\n     * @description B.foo2\n     */\n    foo2: (para2: string) => number;\n}\n\n// implement multi interfaces with duplicate name\n// method for function signature\nclass C implements A, B {\n    /*1*/foo1: number = 1;\n    /*2*/foo2(q: string) { return 1 }\n}\n\n// implement multi interfaces with duplicate name\n// property for function signature\nclass D implements A, B {\n    /*3*/foo1: number = 1;\n    /*4*/foo2 = (q: string) => { return 1 }\n}\n\nnew C()./*5*/foo1;\nnew C()./*6*/foo2;\nnew D()./*7*/foo1;\nnew D()./*8*/foo2;\n\nclass Base1 {\n    /**\n     * @description Base1.foo1 \n     */\n    foo1: number = 1;\n\n    /**\n     * \n     * @param q Base1.foo2 parameter\n     * @returns Base1.foo2 return\n     */\n     foo2(q: string) { return 1 }\n}\n\n// extends class and implement interfaces with duplicate name\n// property override method\nclass Drived1 extends Base1 implements A {\n    /*9*/foo1: number = 1;\n    /*10*/foo2(para1: string) { return 1 };\n}\n\n// extends class and implement interfaces with duplicate name\n// method override method\nclass Drived2 extends Base1 implements B {\n    /*11*/foo1: number = 1;\n    /*12*/foo2 = (para1: string) => { return 1; };\n}\n\nclass Base2 {\n    /**\n     * @description Base2.foo1 \n     */\n    foo1: number = 1;\n    /**\n     * \n     * @param q Base2.foo2 parameter\n     * @returns Base2.foo2 return\n     */\n    foo2(q: string) { return 1 }\n}\n\n// extends class and implement interfaces with duplicate name\n// property override method\nclass Drived3 extends Base2 implements A {\n    /*13*/foo1: number = 1;\n    /*14*/foo2(para1: string) { return 1 };\n}\n\n// extends class and implement interfaces with duplicate name\n// method override method\nclass Drived4 extends Base2 implements B {\n    /*15*/foo1: number = 1;\n    /*16*/foo2 = (para1: string) => { return 1; };\n}\n\nnew Drived1()./*17*/foo1;\nnew Drived1()./*18*/foo2;\nnew Drived2()./*19*/foo1;\nnew Drived2()./*20*/foo2;\nnew Drived3()./*21*/foo1;\nnew Drived3()./*22*/foo2;\nnew Drived4()./*23*/foo1;\nnew Drived4()./*24*/foo2;"u8);
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
