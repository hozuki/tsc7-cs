// ported from: internal/fourslash/tests/quickInfoDisplayPartsVar_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsVarCase
{
    public static void TestQuickInfoDisplayPartsVar(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var /*1*/a = 10;\nfunction foo() {\n    var /*2*/b = /*3*/a;\n}\nnamespace m {\n    var /*4*/c = 10;\n    export var /*5*/d = 10;\n}\nvar /*6*/f: () => number;\nvar /*7*/g = /*8*/f;\n/*9*/f();\nvar /*10*/h: { (a: string): number; (a: number): string; };\nvar /*11*/i = /*12*/h;\n/*13*/h(10);\n/*14*/h(\"hello\");"u8);
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
