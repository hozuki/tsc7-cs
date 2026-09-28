// ported from: internal/fourslash/tests/signatureHelpCommentsFunctionExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpCommentsFunctionExpressionCase
{
    public static void TestSignatureHelpCommentsFunctionExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** lambdaFoo var comment*/\nvar lambdaFoo = /** this is lambda comment*/ (/**param a*/a: number, /**param b*/b: number) => a + b;\nvar lambddaNoVarComment = /** this is lambda multiplication*/ (/**param a*/a: number, /**param b*/b: number) => a * b;\nlambdaFoo(/*5*/10, /*6*/20);\nfunction anotherFunc(a: number) {\n    /** documentation\n        @param b {string} inner parameter */\n    var lambdaVar = /** inner docs */(b: string) => {\n        var localVar = \"Hello \";\n        return localVar + b;\n    }\n    return lambdaVar(\"World\") + a;\n}\n/**\n * On variable\n * @param s the first parameter!\n * @returns the parameter's length\n */\nvar assigned = /**\n                * Summary on expression\n                * @param s param on expression\n                * @returns return on expression\n                */function(/** On parameter */s: string) {\n  return s.length;\n}\nassigned(/*18*/\"hey\");"u8);
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
