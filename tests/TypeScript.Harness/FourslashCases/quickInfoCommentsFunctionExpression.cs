// ported from: internal/fourslash/tests/quickInfoCommentsFunctionExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoCommentsFunctionExpressionCase
{
    public static void TestQuickInfoCommentsFunctionExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** lambdaFoo var comment*/\nvar lamb/*1*/daFoo = /** this is lambda comment*/ (/**param a*/a: number, /**param b*/b: number) => a + b;\nvar lambddaN/*3*/oVarComment = /** this is lambda multiplication*/ (/**param a*/a: number, /**param b*/b: number) => a * b;\nlambdaFoo(10, 20);\nfunction /*7*/anotherFunc(a: number) {\n    /** documentation\n        @param b {string} inner parameter */\n    var /*8*/lambdaVar = /** inner docs */(/*9*/b: string) => {\n        var /*10*/localVar = \"Hello \";\n        return /*11*/localVar + /*12*/b;\n    }\n    return lamb/*13*/daVar(\"World\") + a;\n}\n/**\n * On variable\n * @param s the first parameter!\n * @returns the parameter's length\n */\nvar assi/*14*/gned = /**\n                * Summary on expression\n                * @param s param on expression\n                * @returns return on expression\n                */function(/** On parameter */s: string) {\n  return s.length;\n}\nassig/*16*/ned(\"hey\");"u8);
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
