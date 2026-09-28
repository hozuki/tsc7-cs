// ported from: internal/fourslash/tests/goToTypeDefinitionModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinitionModuleCase
{
    public static void TestGoToTypeDefinitionModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: module1.ts\nmodule /*definition*/M {\n    export var p;\n}\nvar m: typeof M;\n// @Filename: module3.ts\n/*reference1*/M;\n/*reference2*/m;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference1", "reference2");
            }
            finally
            {
                done();
            }
        });
    }

}
