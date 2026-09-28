// ported from: internal/fourslash/tests/localGetReferences_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LocalGetReferencesCase
{
    public static void TestLocalGetReferences(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: localGetReferences_1.ts\n// Comment Refence Test: g/*43*/lobalVar\n// References to a variable declared in global.\n/*1*/var /*2*/globalVar: number = 2;\n\nclass fooCls {\n    // References to static variable declared in a class.\n    /*3*/static /*4*/clsSVar = 1;\n    // References to a variable declared in a class.\n    /*5*/clsVar = 1;\n\n    constructor (/*6*/public /*7*/clsParam: number) {\n        //Increments\n        /*8*/globalVar++;\n        this./*9*/clsVar++;\n        fooCls./*10*/clsSVar++;\n        // References to a class parameter.\n        this./*11*/clsParam++;\n        modTest.modVar++;\n    }\n}\n\n// References to a function parameter.\n/*12*/function /*13*/foo(/*14*/x: number) {\n    // References to a variable declared in a function.\n    /*15*/var /*16*/fnVar = 1;\n\n    //Increments\n    fooCls./*17*/clsSVar++;\n    /*18*/globalVar++;\n    modTest.modVar++;\n    /*19*/fnVar++;\n\n    //Return\n    return /*20*/x++;\n}\n\nnamespace modTest {\n    //Declare\n    export var modVar:number;\n\n    //Increments\n    /*21*/globalVar++;\n    fooCls./*22*/clsSVar++;\n    modVar++;\n\n    class testCls {\n        static boo = /*23*/foo;\n    }\n\n    function testFn(){\n        static boo = /*24*/foo;\n\n        //Increments\n        /*25*/globalVar++;\n        fooCls./*26*/clsSVar++;\n        modVar++;\n    }\n\n    namespace testMod {\n        var boo = /*27*/foo;\n    }\n}\n\n//Type test\nvar clsTest: fooCls;\n\n//Arguments\n// References to a class argument.\nclsTest = new fooCls(/*28*/globalVar);\n// References to a function argument.\n/*29*/foo(/*30*/globalVar);\n\n//Increments\nfooCls./*31*/clsSVar++;\nmodTest.modVar++;\n/*32*/globalVar = /*33*/globalVar + /*34*/globalVar;\n\n//ETC - Other cases\n/*35*/globalVar = 3;\n// References to illegal assignment.\n/*36*/foo = /*37*/foo + 1;\n/*44*/err = err++;\n/*45*/\n//Shadowed fn Parameter\nfunction shdw(/*38*/globalVar: number) {\n    //Increments\n    /*39*/globalVar++;\n    return /*40*/globalVar;\n}\n\n//Remotes\n//Type test\nvar remoteclsTest: remotefooCls;\n\n//Arguments\nremoteclsTest = new remotefooCls(remoteglobalVar);\nremotefoo(remoteglobalVar);\n\n//Increments\nremotefooCls.remoteclsSVar++;\nremotemodTest.remotemodVar++;\nremoteglobalVar = remoteglobalVar + remoteglobalVar;\n\n//ETC - Other cases\nremoteglobalVar = 3;\n\n//Find References misses method param\nvar\n\n\n\n array = [\"f\", \"o\", \"o\"];\n\narray.forEach(\n\n\nfunction(/*41*/str) {\n\n\n\n   // Reference misses function parameter.\n   return /*42*/str + \" \";\n\n});\n// @Filename: localGetReferences_2.ts\nvar remoteglobalVar: number = 2;\n\nclass remotefooCls {\n\t//Declare\n\tremoteclsVar = 1;\n\tstatic remoteclsSVar = 1;\n\n\tconstructor(public remoteclsParam: number) {\n\t\t//Increments\n\t\tremoteglobalVar++;\n\t\tthis.remoteclsVar++;\n\t\tremotefooCls.remoteclsSVar++;\n\t\tthis.remoteclsParam++;\n\t\tremotemodTest.remotemodVar++;\n\t}\n}\n\nfunction remotefoo(remotex: number) {\n\t//Declare\n\tvar remotefnVar = 1;\n\n\t//Increments\n\tremotefooCls.remoteclsSVar++;\n\tremoteglobalVar++;\n\tremotemodTest.remotemodVar++;\n\tremotefnVar++;\n\n\t//Return\n\treturn remotex++;\n}\n\nnamespace remotemodTest {\n\t//Declare\n\texport var remotemodVar: number;\n\n\t//Increments\n\tremoteglobalVar++;\n\tremotefooCls.remoteclsSVar++;\n\tremotemodVar++;\n\n\tclass remotetestCls {\n\t\tstatic remoteboo = remotefoo;\n\t}\n`\n\tfunction remotetestFn(){\n        static remoteboo = remotefoo;\n\n\t\t//Increments\n\t\tremoteglobalVar++;\n\t\tremotefooCls.remoteclsSVar++;\n\t\tremotemodVar++;\n    }\n\n\tnamespace remotetestMod {\n\t\tvar remoteboo = remotefoo;\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "31", "32", "33", "34", "35", "36", "37", "38", "39", "40", "41", "42", "43", "44", "45");
            }
            finally
            {
                done();
            }
        });
    }

}
