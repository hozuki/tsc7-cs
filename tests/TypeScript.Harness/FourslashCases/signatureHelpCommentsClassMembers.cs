// ported from: internal/fourslash/tests/signatureHelpCommentsClassMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpCommentsClassMembersCase
{
    public static void TestSignatureHelpCommentsClassMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** This is comment for c1*/\nclass c1 {\n    /** p1 is property of c1*/\n    public p1: number;\n    /** sum with property*/\n    public p2(/** number to add*/b: number) {\n        return this.p1 + b;\n    }\n    /** getter property 1*/\n    public get p3() {\n        return this.p2(/*8*/this.p1);\n    }\n    /** setter property 1*/\n    public set p3(/** this is value*/value: number) {\n        this.p1 = this.p2(/*13*/value);\n    }\n    /** pp1 is property of c1*/\n    private pp1: number;\n    /** sum with property*/\n    private pp2(/** number to add*/b: number) {\n        return this.p1 + b;\n    }\n    /** getter property 2*/\n    private get pp3() {\n        return this.pp2(/*20*/this.pp1);\n    }\n    /** setter property 2*/\n    private set pp3( /** this is value*/value: number) {\n        this.pp1 = this.pp2(/*25*/value);\n    }\n    /** Constructor method*/\n    constructor() {\n    }\n    /** s1 is static property of c1*/\n    static s1: number;\n    /** static sum with property*/\n    static s2(/** number to add*/b: number) {\n        return c1.s1 + b;\n    }\n    /** static getter property*/\n    static get s3() {\n        return c1.s2(/*35*/c1.s1);\n    }\n    /** setter property 3*/\n    static set s3( /** this is value*/value: number) {\n        c1.s1 = c1.s2(/*42*/value);\n    }\n    public nc_p1: number;\n    public nc_p2(b: number) {\n        return this.nc_p1 + b;\n    }\n    public get nc_p3() {\n        return this.nc_p2(/*47*/this.nc_p1);\n    }\n    public set nc_p3(value: number) {\n        this.nc_p1 = this.nc_p2(/*49*/value);\n    }\n    private nc_pp1: number;\n    private nc_pp2(b: number) {\n        return this.nc_pp1 + b;\n    }\n    private get nc_pp3() {\n        return this.nc_pp2(/*54*/this.nc_pp1);\n    }\n    private set nc_pp3(value: number) {\n        this.nc_pp1 = this.nc_pp2(/*56*/value);\n    }\n    static nc_s1: number;\n    static nc_s2(b: number) {\n        return c1.nc_s1 + b;\n    }\n    static get nc_s3() {\n        return c1.nc_s2(/*61*/c1.nc_s1);\n    }\n    static set nc_s3(value: number) {\n        c1.nc_s1 = c1.nc_s2(/*63*/value);\n    }\n}\nvar i1 = new c1(/*65*/);\nvar i1_p = i1.p1;\nvar i1_f = i1.p2;\nvar i1_r = i1.p2(/*71*/20);\nvar i1_prop = i1.p3;\ni1.p3 = i1_prop;\nvar i1_nc_p = i1.nc_p1;\nvar i1_ncf = i1.nc_p2;\nvar i1_ncr = i1.nc_p2(/*81*/20);\nvar i1_ncprop = i1.nc_p3;\ni1.nc_p3 = i1_ncprop;\nvar i1_s_p = c1.s1;\nvar i1_s_f = c1.s2;\nvar i1_s_r = c1.s2(/*92*/20);\nvar i1_s_prop = c1.s3;\nc1.s3 = i1_s_prop;\nvar i1_s_nc_p = c1.nc_s1;\nvar i1_s_ncf = c1.nc_s2;\nvar i1_s_ncr = c1.nc_s2(/*102*/20);\nvar i1_s_ncprop = c1.nc_s3;\nc1.nc_s3 = i1_s_ncprop;\nvar i1_c = c1;\n\nclass cProperties {\n    private val: number;\n    /** getter only property*/\n    public get p1() {\n        return this.val;\n    }\n    public get nc_p1() {\n        return this.val;\n    }\n    /**setter only property*/\n    public set p2(value: number) {\n        this.val = value;\n    }\n    public set nc_p2(value: number) {\n        this.val = value;\n    }\n}\nvar cProperties_i = new cProperties();\ncProperties_i.p2 = cProperties_i.p1;\ncProperties_i.nc_p2 = cProperties_i.nc_p1;\nclass cWithConstructorProperty {\n    /**\n    * this is class cWithConstructorProperty's constructor\n    * @param a this is first parameter a\n    */\n    constructor(/**more info about a*/public a: number) {\n        var bbbb = 10;\n        this.a = a + 2 + bbbb;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
