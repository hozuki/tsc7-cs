// ported from: internal/fourslash/tests/quickInfoDisplayPartsClassConstructor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsClassConstructorCase
{
    public static void TestQuickInfoDisplayPartsClassConstructor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class c {\n    /*1*/constructor() {\n    }\n}\nvar /*2*/cInstance = new /*3*/c();\nvar /*4*/cVal = /*5*/c;\nclass cWithOverloads {\n    /*6*/constructor(x: string);\n    /*7*/constructor(x: number);\n    /*8*/constructor(x: any) {\n    }\n}\nvar /*9*/cWithOverloadsInstance = new /*10*/cWithOverloads(\"hello\");\nvar /*11*/cWithOverloadsInstance2 = new /*12*/cWithOverloads(10);\nvar /*13*/cWithOverloadsVal = /*14*/cWithOverloads;\nclass cWithMultipleOverloads {\n    /*15*/constructor(x: string);\n    /*16*/constructor(x: number);\n    /*17*/constructor(x: boolean);\n    /*18*/constructor(x: any) {\n    }\n}\nvar /*19*/cWithMultipleOverloadsInstance = new /*20*/cWithMultipleOverloads(\"hello\");\nvar /*21*/cWithMultipleOverloadsInstance2 = new /*22*/cWithMultipleOverloads(10);\nvar /*23*/cWithMultipleOverloadsInstance3 = new /*24*/cWithMultipleOverloads(true);\nvar /*25*/cWithMultipleOverloadsVal = /*26*/cWithMultipleOverloads;"u8);
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
