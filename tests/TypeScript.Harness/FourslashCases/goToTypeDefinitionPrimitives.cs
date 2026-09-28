// ported from: internal/fourslash/tests/goToTypeDefinitionPrimitives_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinitionPrimitivesCase
{
    public static void TestGoToTypeDefinitionPrimitives(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @Filename: module1.ts\nvar w: {a: number};\nvar x = \"string\";\nvar y: number | string;\nvar z; // any\n// @Filename: module2.ts\nw./*reference1*/a;\n/*reference2*/x;\n/*reference3*/y;\n/*reference4*/y;";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference1", "reference2", "reference3", "reference4");
            }
            finally
            {
                done();
            }
        });
    }

}
