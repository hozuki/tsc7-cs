// ported from: internal/fourslash/tests/getPreProcessedFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetPreProcessedFileCase
{
    public static void TestGetPreProcessedFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: classic\n// @Filename: refFile1.ts\nclass D { }\n// @Filename: refFile2.ts\nexport class E {}\n// @Filename: main.ts\n// @ResolveReference: true\n///<reference path=\"refFile1.ts\" />\n///<reference path = \"/*1*/NotExistRef.ts/*2*/\" />\n/*3*////<reference path \"invalidRefFile1.ts\" />/*4*/\nimport ref2 = require(\"refFile2\");\nimport noExistref2 = require(/*5*/\"NotExistRefFile2\"/*6*/);\nimport invalidRef1  /*7*/require/*8*/(\"refFile2\");\nimport invalidRef2 = /*9*/requi/*10*/(/*10A*/\"refFile2\");\nvar obj: /*11*/C/*12*/;\nvar obj1: D;\nvar obj2: ref2.E;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "main.ts");
                f.VerifyNumberOfErrorsInCurrentFile(t, 7);
                f.VerifyErrorExistsBetweenMarkers(t, "1", "2");
                f.VerifyErrorExistsBetweenMarkers(t, "3", "4");
                f.VerifyErrorExistsBetweenMarkers(t, "5", "6");
                f.VerifyErrorExistsBetweenMarkers(t, "7", "8");
                f.VerifyErrorExistsBetweenMarkers(t, "9", "10");
                f.VerifyErrorExistsBetweenMarkers(t, "10", "10A");
                f.VerifyErrorExistsBetweenMarkers(t, "11", "12");
            }
            finally
            {
                done();
            }
        });
    }

}
