// ported from: internal/fourslash/tests/quickInfoDisplayPartsFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsFunctionCase
{
    public static void TestQuickInfoDisplayPartsFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function /*1*/foo(param: string, optionalParam?: string, paramWithInitializer = \"hello\", ...restParam: string[]) {\n}\nfunction /*2*/foowithoverload(a: string): string;\nfunction /*3*/foowithoverload(a: number): number;\nfunction /*4*/foowithoverload(a: any): any {\n    return a;\n}\nfunction /*5*/foowith3overload(a: string): string;\nfunction /*6*/foowith3overload(a: number): number;\nfunction /*7*/foowith3overload(a: boolean): boolean;\nfunction /*8*/foowith3overload(a: any): any {\n    return a;\n}\n/*9*/foo(\"hello\");\n/*10*/foowithoverload(\"hello\");\n/*11*/foowithoverload(10);\n/*12*/foowith3overload(\"hello\");\n/*13*/foowith3overload(10);\n/*14*/foowith3overload(true);"u8);
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
