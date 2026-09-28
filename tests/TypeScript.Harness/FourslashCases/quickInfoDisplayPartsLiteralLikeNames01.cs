// ported from: internal/fourslash/tests/quickInfoDisplayPartsLiteralLikeNames01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsLiteralLikeNames01Case
{
    public static void TestQuickInfoDisplayPartsLiteralLikeNames01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    public /*1*/1() { }\n    private /*2*/Infinity() { }\n    protected /*3*/NaN() { }\n    static /*4*/\"stringLiteralName\"() { }\n    method() {\n        this[/*5*/1]();\n        this[/*6*/\"1\"]();\n        this./*7*/Infinity();\n        this[/*8*/\"Infinity\"]();\n        this./*9*/NaN();\n        C./*10*/stringLiteralName();\n    }"u8);
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
