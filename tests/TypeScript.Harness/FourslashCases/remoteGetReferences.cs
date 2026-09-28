// ported from: internal/fourslash/tests/remoteGetReferences_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RemoteGetReferencesCase
{
    public static void TestRemoteGetReferences(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: remoteGetReferences_1.ts\n// Comment Refence Test: globalVar\nvar globalVar: number = 2;\n\nclass fooCls {\n    static clsSVar = 1;\n    //Declare\n    clsVar = 1;\n\n    constructor (public clsParam: number) {\n        //Increments\n        globalVar++;\n        this.clsVar++;\n        fooCls.clsSVar++;\n        this.clsParam++;\n        modTest.modVar++;\n    }\n}\n\nfunction foo(x: number) {\n    //Declare\n    var fnVar = 1;\n\n    //Increments\n    fooCls.clsSVar++;\n    globalVar++;\n    modTest.modVar++;\n    fnVar++;\n\n    //Return\n    return x++;\n}\n\nnamespace modTest {\n    //Declare\n    export var modVar:number;\n\n    //Increments\n    globalVar++;\n    fooCls.clsSVar++;\n    modVar++;\n\n    class testCls {\n        static boo = foo;\n    }\n\n    function testFn(){\n        static boo = foo;\n\n        //Increments\n        globalVar++;\n        fooCls.clsSVar++;\n        modVar++;\n    }\n\n    namespace testMod {\n        var boo = foo;\n    }\n}\n\n//Type test\nvar clsTest: fooCls;\n\n//Arguments\nclsTest = new fooCls(globalVar);\nfoo(globalVar);\n\n//Increments\nfooCls.clsSVar++;\nmodTest.modVar++;\nglobalVar = globalVar + globalVar;\n\n//ETC - Other cases\nglobalVar = 3;\nfoo = foo + 1;\nerr = err++;\n\n//Shadowed fn Parameter\nfunction shdw(globalVar: number) {\n    //Increments\n    globalVar++;\n    return globalVar;\n}\n\n//Remotes\n//Type test\nvar remoteclsTest: /*1*/remotefooCls;\n\n//Arguments\nremoteclsTest = new /*2*/remotefooCls(/*3*/remoteglobalVar);\nremotefoo(/*4*/remoteglobalVar);\n\n//Increments\n/*5*/remotefooCls./*6*/remoteclsSVar++;\nremotemodTest.remotemodVar++;\n/*7*/remoteglobalVar = /*8*/remoteglobalVar + /*9*/remoteglobalVar;\n\n//ETC - Other cases\n/*10*/remoteglobalVar = 3;\n\n//Find References misses method param\nvar\n\n\n\n array = [\"f\", \"o\", \"o\"];\n\narray.forEach(\n\n\nfunction(str) {\n\n\n\n   return str + \" \";\n\n});\n// @Filename: remoteGetReferences_2.ts\n/*11*/var /*12*/remoteglobalVar: number = 2;\n\n/*13*/class /*14*/remotefooCls {\n\t//Declare\n\t/*15*/remoteclsVar = 1;\n\t/*16*/static /*17*/remoteclsSVar = 1;\n\n\tconstructor(public remoteclsParam: number) {\n\t\t//Increments\n\t\t/*18*/remoteglobalVar++;\n\t\tthis./*19*/remoteclsVar++;\n\t\t/*20*/remotefooCls./*21*/remoteclsSVar++;\n\t\tthis.remoteclsParam++;\n\t\tremotemodTest.remotemodVar++;\n\t}\n}\n\nfunction remotefoo(remotex: number) {\n\t//Declare\n\tvar remotefnVar = 1;\n\n\t//Increments\n\t/*22*/remotefooCls./*23*/remoteclsSVar++;\n\t/*24*/remoteglobalVar++;\n\tremotemodTest.remotemodVar++;\n\tremotefnVar++;\n\n\t//Return\n\treturn remotex++;\n}\n\nnamespace remotemodTest {\n\t//Declare\n\texport var remotemodVar: number;\n\n\t//Increments\n\t/*25*/remoteglobalVar++;\n\t/*26*/remotefooCls./*27*/remoteclsSVar++;\n\tremotemodVar++;\n\n\tclass remotetestCls {\n\t\tstatic remoteboo = remotefoo;\n\t}\n\n\tfunction remotetestFn(){\n        static remoteboo = remotefoo;\n\n\t\t//Increments\n\t\t/*28*/remoteglobalVar++;\n\t\t/*29*/remotefooCls./*30*/remoteclsSVar++;\n\t\tremotemodVar++;\n    }\n\n\tnamespace remotetestMod {\n\t\tvar remoteboo = remotefoo;\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30");
            }
            finally
            {
                done();
            }
        });
    }

}
