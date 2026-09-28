// ported from: internal/fourslash/tests/jsDocFunctionSignatures12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocFunctionSignatures12Case
{
    public static void TestJsDocFunctionSignatures12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: jsDocFunctionSignatures.js\n/**\n * @param {{\n *   stringProp: string,\n *   numProp: number,\n *   boolProp: boolean,\n *   anyProp: any,\n *   anotherAnyProp: any,\n *   functionProp: (arg0: string, arg1: any) => any\n * }} o\n */\nfunction f1(o) {\n    o/**/;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "(parameter) o: {\n    stringProp: string;\n    numProp: number;\n    boolProp: boolean;\n    anyProp: any;\n    anotherAnyProp: any;\n    functionProp: (arg0: string, arg1: any) => any;\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
