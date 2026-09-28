// ported from: internal/fourslash/tests/goToDefinitionSourceUnit_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionSourceUnitCase
{
    public static void TestGoToDefinitionSourceUnit(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n //MyFile Comments\n //more comments\n /// <reference path=\"so/*unknownFile*/mePath.ts\" />\n /// <reference path=\"[|b/*knownFile*/.ts|]\" />\n\n class clsInOverload {\n     static fnOverload();\n     static fnOverload(foo: string);\n     static fnOverload(foo: any) { }\n }\n\n// @Filename: b.ts\n/*fileB*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "unknownFile", "knownFile");
            }
            finally
            {
                done();
            }
        });
    }

}
