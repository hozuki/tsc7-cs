// ported from: internal/fourslash/tests/formattingOnClasses_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnClassesCase
{
    public static void TestFormattingOnClasses(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/         class                    a                  {\n/*2*/                                                        constructor       (       n   :                 number    )             ;\n/*3*/                                                        constructor       (       s   :                 string    )             ;\n/*4*/                                                        constructor       (       ns   :                 any    )                            {\n\n/*5*/                                                        }\n\n/*6*/                                                            public                 pgF       (           )                            {                  }\n\n/*7*/                                                            public                 pv   ;\n/*8*/                                                            public                 get              d       (           )                            {\n/*9*/                                                                                                                return              30   ;\n/*10*/                                                        }\n/*11*/                                                            public                 set              d       (       number        )                            {\n/*12*/                                                        }\n\n/*13*/                                                            public                 static                    get              p2       (           )                            {\n/*14*/                                                                                                                return                  {                  x   :                 30   ,                  y   :                 40              }   ;\n/*15*/                                                        }\n\n/*16*/                                                                         private                static                    d2       (           )                            {\n/*17*/                                                        }\n/*18*/                                                                         private                static                    get              p3       (           )                            {\n/*19*/                                                                                                                return              \"string\"   ;\n/*20*/                                                        }\n/*21*/                                                                         private                pv3   ;\n\n/*22*/                                                                         private                foo       (       n   :                 number    )             :                 string   ;\n/*23*/                                                                         private                foo       (       s   :                 string    )             :                 string   ;\n/*24*/                                                                         private                foo       (       ns   :                 any    )                            {\n/*25*/                                                                                                                return              ns.toString       (           )             ;\n/*26*/                                                        }\n/*27*/}\n\n/*28*/         class                    b              extends              a                  {\n/*29*/}\n\n/*30*/         class   m1b      {\n\n/*31*/}\n\n/*32*/                                                interface   m1ib                               {\n\n/*33*/  }\n/*34*/         class                    c              extends              m1b                  {\n/*35*/}\n\n/*36*/         class                    ib2              implements              m1ib                  {\n/*37*/}\n\n/*38*/    declare                            class                    aAmbient                  {\n/*39*/                                                        constructor                     (       n   :                 number    )             ;\n/*40*/                                                        constructor                     (       s   :                 string    )             ;\n/*41*/                                                            public                 pgF       (           )             :                 void   ;\n/*42*/                                                            public                 pv   ;\n/*43*/                                                            public                 d                 :                 number   ;\n/*44*/                                                        static                    p2                 :                     {                  x   :                 number   ;              y   :                 number   ;              }   ;\n/*45*/                                                        static                    d2       (           )             ;\n/*46*/                                                        static                    p3   ;\n/*47*/                                                                         private                pv3   ;\n/*48*/                                                                         private                foo       (       s    )             ;\n/*49*/}\n\n/*50*/         class                    d                  {\n/*51*/                                                                         private                foo       (       n   :                 number    )             :                 string   ;\n/*52*/                                                                         private                foo       (       s   :                 string    )             :                 string   ;\n/*53*/                                                                         private                foo       (       ns   :                 any    )                            {\n/*54*/                                                                                                                return              ns.toString       (           )             ;\n/*55*/                                                        }\n/*56*/}\n\n/*57*/         class                    e                  {\n/*58*/                                                                         private                foo       (       s   :                 string    )             :                 string   ;\n/*59*/                                                                         private                foo       (       n   :                 number    )             :                 string   ;\n/*60*/                                                                         private                foo       (       ns   :                 any    )                            {\n/*61*/                                                                                                                return              ns.toString       (           )             ;\n/*62*/                                                        }\n/*63*/                                                                         protected              bar        (            )  {                 }\n/*64*/                                                                         protected     static   bar2       (            )  {                 }\n/*65*/                                                                         private                pv4  :    number =\n/*66*/                                                                         {};\n/*END*/}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "class a {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    constructor(n: number);");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    constructor(s: string);");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    constructor(ns: any) {");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    public pgF() { }");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "    public pv;");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    public get d() {");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "        return 30;");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "    public set d(number) {");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "13");
                f.VerifyCurrentLineContent(t, "    public static get p2() {");
                f.GoToMarker(t, "14");
                f.VerifyCurrentLineContent(t, "        return { x: 30, y: 40 };");
                f.GoToMarker(t, "15");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "16");
                f.VerifyCurrentLineContent(t, "    private static d2() {");
                f.GoToMarker(t, "17");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "18");
                f.VerifyCurrentLineContent(t, "    private static get p3() {");
                f.GoToMarker(t, "19");
                f.VerifyCurrentLineContent(t, "        return \"string\";");
                f.GoToMarker(t, "20");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "21");
                f.VerifyCurrentLineContent(t, "    private pv3;");
                f.GoToMarker(t, "22");
                f.VerifyCurrentLineContent(t, "    private foo(n: number): string;");
                f.GoToMarker(t, "23");
                f.VerifyCurrentLineContent(t, "    private foo(s: string): string;");
                f.GoToMarker(t, "24");
                f.VerifyCurrentLineContent(t, "    private foo(ns: any) {");
                f.GoToMarker(t, "25");
                f.VerifyCurrentLineContent(t, "        return ns.toString();");
                f.GoToMarker(t, "26");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "27");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "28");
                f.VerifyCurrentLineContent(t, "class b extends a {");
                f.GoToMarker(t, "29");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "30");
                f.VerifyCurrentLineContent(t, "class m1b {");
                f.GoToMarker(t, "31");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "32");
                f.VerifyCurrentLineContent(t, "interface m1ib {");
                f.GoToMarker(t, "33");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "34");
                f.VerifyCurrentLineContent(t, "class c extends m1b {");
                f.GoToMarker(t, "35");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "36");
                f.VerifyCurrentLineContent(t, "class ib2 implements m1ib {");
                f.GoToMarker(t, "37");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "38");
                f.VerifyCurrentLineContent(t, "declare class aAmbient {");
                f.GoToMarker(t, "39");
                f.VerifyCurrentLineContent(t, "    constructor(n: number);");
                f.GoToMarker(t, "40");
                f.VerifyCurrentLineContent(t, "    constructor(s: string);");
                f.GoToMarker(t, "41");
                f.VerifyCurrentLineContent(t, "    public pgF(): void;");
                f.GoToMarker(t, "42");
                f.VerifyCurrentLineContent(t, "    public pv;");
                f.GoToMarker(t, "43");
                f.VerifyCurrentLineContent(t, "    public d: number;");
                f.GoToMarker(t, "44");
                f.VerifyCurrentLineContent(t, "    static p2: { x: number; y: number; };");
                f.GoToMarker(t, "45");
                f.VerifyCurrentLineContent(t, "    static d2();");
                f.GoToMarker(t, "46");
                f.VerifyCurrentLineContent(t, "    static p3;");
                f.GoToMarker(t, "47");
                f.VerifyCurrentLineContent(t, "    private pv3;");
                f.GoToMarker(t, "48");
                f.VerifyCurrentLineContent(t, "    private foo(s);");
                f.GoToMarker(t, "49");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "50");
                f.VerifyCurrentLineContent(t, "class d {");
                f.GoToMarker(t, "51");
                f.VerifyCurrentLineContent(t, "    private foo(n: number): string;");
                f.GoToMarker(t, "52");
                f.VerifyCurrentLineContent(t, "    private foo(s: string): string;");
                f.GoToMarker(t, "53");
                f.VerifyCurrentLineContent(t, "    private foo(ns: any) {");
                f.GoToMarker(t, "54");
                f.VerifyCurrentLineContent(t, "        return ns.toString();");
                f.GoToMarker(t, "55");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "56");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "57");
                f.VerifyCurrentLineContent(t, "class e {");
                f.GoToMarker(t, "58");
                f.VerifyCurrentLineContent(t, "    private foo(s: string): string;");
                f.GoToMarker(t, "59");
                f.VerifyCurrentLineContent(t, "    private foo(n: number): string;");
                f.GoToMarker(t, "60");
                f.VerifyCurrentLineContent(t, "    private foo(ns: any) {");
                f.GoToMarker(t, "61");
                f.VerifyCurrentLineContent(t, "        return ns.toString();");
                f.GoToMarker(t, "62");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "63");
                f.VerifyCurrentLineContent(t, "    protected bar() { }");
                f.GoToMarker(t, "64");
                f.VerifyCurrentLineContent(t, "    protected static bar2() { }");
                f.GoToMarker(t, "65");
                f.VerifyCurrentLineContent(t, "    private pv4: number =");
                f.GoToMarker(t, "66");
                f.VerifyCurrentLineContent(t, "        {};");
                f.GoToMarker(t, "END");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
