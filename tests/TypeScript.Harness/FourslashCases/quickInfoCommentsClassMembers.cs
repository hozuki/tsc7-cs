// ported from: internal/fourslash/tests/quickInfoCommentsClassMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoCommentsClassMembersCase
{
    public static void TestQuickInfoCommentsClassMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** This is comment for c1*/\nclass c/*1*/1 {\n    /** p1 is property of c1*/\n    public p/*2*/1: number;\n    /** sum with property*/\n    public p/*3*/2(/** number to add*/b: number) {\n        return this.p1 + b;\n    }\n    /** getter property 1*/\n    public get p/*6*/3() {\n        return this.p/*8q*/2(this.p1);\n    }\n    /** setter property 1*/\n    public set p/*10*/3(/** this is value*/value: number) {\n        this.p1 = this.p/*13q*/2(value);\n    }\n    /** pp1 is property of c1*/\n    private p/*14*/p1: number;\n    /** sum with property*/\n    private p/*15*/p2(/** number to add*/b: number) {\n        return this.p1 + b;\n    }\n    /** getter property 2*/\n    private get p/*18*/p3() {\n        return this.p/*20q*/p2(this.pp1);\n    }\n    /** setter property 2*/\n    private set p/*22*/p3( /** this is value*/value: number) {\n        this.pp1 = this.p/*25q*/p2(value);\n    }\n    /** Constructor method*/\n    constru/*26*/ctor() {\n    }\n    /** s1 is static property of c1*/\n    static s/*27*/1: number;\n    /** static sum with property*/\n    static s/*28*/2(/** number to add*/b: number) {\n        return c1.s1 + b;\n    }\n    /** static getter property*/\n    static get s/*32*/3() {\n        return c1.s/*35q*/2(c1.s1);\n    }\n    /** setter property 3*/\n    static set s/*37*/3( /** this is value*/value: number) {\n        c1.s1 = c1.s/*42q*/2(value);\n    }\n    public nc_/*43*/p1: number;\n    public nc_/*44*/p2(b: number) {\n        return this.nc_p1 + b;\n    }\n    public get nc_/*46*/p3() {\n        return this.nc/*47q*/_p2(this.nc_p1);\n    }\n    public set nc/*48*/_p3(value: number) {\n        this.nc_p1 = this.nc/*49q*/_p2(value);\n    }\n    private nc/*50*/_pp1: number;\n    private nc_/*51*/pp2(b: number) {\n        return this.nc_pp1 + b;\n    }\n    private get nc/*53*/_pp3() {\n        return this.nc_/*54q*/pp2(this.nc_pp1);\n    }\n    private set nc_p/*55*/p3(value: number) {\n        this.nc_pp1 = this./*56q*/nc_pp2(value);\n    }\n    static nc/*57*/_s1: number;\n    static nc/*58*/_s2(b: number) {\n        return c1.nc_s1 + b;\n    }\n    static get nc/*60*/_s3() {\n        return c1.nc/*61q*/_s2(c1.nc_s1);\n    }\n    static set nc/*62*/_s3(value: number) {\n        c1.nc_s1 = c1.nc_/*63q*/s2(value);\n    }\n}\nvar i/*64*/1 = new c/*65q*/1();\nvar i1/*66*/_p = i1.p1;\nvar i1/*68*/_f = i1.p/*69*/2;\nvar i1/*70*/_r = i1.p/*71q*/2(20);\nvar i1_p/*72*/rop = i1./*73*/p3;\ni1./*74*/p3 = i1_/*75*/prop;\nvar i1_/*76*/nc_p = i1.n/*77*/c_p1;\nvar i1/*78*/_ncf = i1.nc_/*79*/p2;\nvar i1_/*80*/ncr = i1.nc/*81q*/_p2(20);\nvar i1_n/*82*/cprop = i1.n/*83*/c_p3;\ni1.nc/*84*/_p3 = i1_/*85*/ncprop;\nvar i1_/*86*/s_p = /*87*/c1./*88*/s1;\nvar i1_s/*89*/_f = c1./*90*/s2;\nvar i1_/*91*/s_r = c1.s/*92q*/2(20);\nvar i1_s/*93*/_prop = c1.s/*94*/3;\nc1.s/*95*/3 = i1_s/*96*/_prop;\nvar i1_s/*97*/_nc_p = c1.n/*98*/c_s1;\nvar i1_s_/*99*/ncf = c1.nc/*100*/_s2;\nvar i1_s_/*101*/ncr = c1.n/*102q*/c_s2(20);\nvar i1_s_n/*103*/cprop = c1.nc/*104*/_s3;\nc1.nc/*105*/_s3 = i1_s_nc/*106*/prop;\nvar i1/*107*/_c = c/*108*/1;\n\nclass cProperties {\n    private val: number;\n    /** getter only property*/\n    public get p1() {\n        return this.val;\n    }\n    public get nc_p1() {\n        return this.val;\n    }\n    /**setter only property*/\n    public set p2(value: number) {\n        this.val = value;\n    }\n    public set nc_p2(value: number) {\n        this.val = value;\n    }\n}\nvar cProperties_i = new cProperties();\ncProperties_i./*110*/p2 = cProperties_i.p/*111*/1;\ncProperties_i.nc/*112*/_p2 = cProperties_i.nc/*113*/_p1;\nclass cWithConstructorProperty {\n    /**\n    * this is class cWithConstructorProperty's constructor\n    * @param a this is first parameter a\n    */\n    /*119*/constructor(/**more info about a*/public a: number) {\n        var b/*118*/bbb = 10;\n        th/*116*/is./*114*/a = /*115*/a + 2 + bb/*117*/bb;\n    }\n}"u8);
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
