// ported from: internal/fourslash/tests/quickInfoCommentsClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoCommentsClassCase
{
    public static void TestQuickInfoCommentsClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** This is class c2 without constructor*/\nclass c/*1*/2 {\n}\nvar i/*2*/2 = new c/*28*/2();\nvar i2/*4*/_c = c/*5*/2;\nclass c/*6*/3 {\n    /** Constructor comment*/\n    constructor() {\n    }\n}\nvar i/*7*/3 = new c/*29*/3();\nvar i3/*9*/_c = c/*10*/3;\n/** Class comment*/\nclass c/*11*/4 {\n    /** Constructor comment*/\n    constructor() {\n    }\n}\nvar i/*12*/4 = new c/*30*/4();\nvar i4/*14*/_c = c/*15*/4;\n/** Class with statics*/\nclass c/*16*/5 {\n    static s1: number;\n}\nvar i/*17*/5 = new c/*31*/5();\nvar i5_/*19*/c = c/*20*/5;\n/** class with statics and constructor*/\nclass c/*21*/6 {\n    /** s1 comment*/\n    static s1: number;\n    /** constructor comment*/\n    constructor() {\n    }\n}\nvar i/*22*/6 = new c/*32*/6();\nvar i6/*24*/_c = c/*25*/6;\n\nclass a {\n    /**\n    constructor for a\n    @param a this is my a\n    */\n    constructor(a: string) {\n    }\n}\nnew a(\"Hello\");\nnamespace m {\n    export namespace m2 {\n        /** class comment */\n        export class c1 {\n            /** constructor comment*/\n            constructor() {\n            }\n        }\n    }\n}\nvar myVar = new m.m2.c/*33*/1();"u8);
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
