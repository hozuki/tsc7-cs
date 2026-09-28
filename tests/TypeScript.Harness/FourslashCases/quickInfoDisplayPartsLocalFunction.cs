// ported from: internal/fourslash/tests/quickInfoDisplayPartsLocalFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsLocalFunctionCase
{
    public static void TestQuickInfoDisplayPartsLocalFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function /*1*/outerFoo() {\n    function /*2*/foo(param: string, optionalParam?: string, paramWithInitializer = \"hello\", ...restParam: string[]) {\n    }\n    function /*3*/foowithoverload(a: string): string;\n    function /*4*/foowithoverload(a: number): number;\n    function /*5*/foowithoverload(a: any): any {\n        return a;\n    }\n    function /*6*/foowith3overload(a: string): string;\n    function /*7*/foowith3overload(a: number): number;\n    function /*8*/foowith3overload(a: boolean): boolean;\n    function /*9*/foowith3overload(a: any): any {\n        return a;\n    }\n    /*10*/foo(\"hello\");\n    /*11*/foowithoverload(\"hello\");\n    /*12*/foowithoverload(10);\n    /*13*/foowith3overload(\"hello\");\n    /*14*/foowith3overload(10);\n    /*15*/foowith3overload(true);\n}\n/*16*/outerFoo();"u8);
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
