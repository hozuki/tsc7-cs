// ported from: internal/fourslash/tests/quickInfoSalsaMethodsOnAssignedFunctionExpressions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoSalsaMethodsOnAssignedFunctionExpressionsCase
{
    public static void TestQuickInfoSalsaMethodsOnAssignedFunctionExpressions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: something.js\nvar C = function () { }\n/**\n * The prototype method.\n * @param {string} a Parameter definition.\n */\nfunction f(a) {}\nC.prototype.m = f;\n\nvar x = new C();\nx/*1*/.m();"u8);
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
