// ported from: internal/fourslash/tests/quickInfoDisplayPartsConst_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsConstCase
{
    public static void TestQuickInfoDisplayPartsConst(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const /*1*/a = 10;\nfunction foo() {\n    const /*2*/b = /*3*/a;\n    if (b) {\n        const /*4*/b1 = 10;\n    }\n}\nnamespace m {\n    const /*5*/c = 10;\n    export const /*6*/d = 10;\n    if (c) {\n        const /*7*/e = 10;\n    }\n}\nconst /*8*/f: () => number = () => 10;\nconst /*9*/g = /*10*/f;\n/*11*/f();\nconst /*12*/h: { (a: string): number; (a: number): string; } = a => a;\nconst /*13*/i = /*14*/h;\n/*15*/h(10);\n/*16*/h(\"hello\");"u8);
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
