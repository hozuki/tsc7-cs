// ported from: internal/fourslash/tests/goToDefinitionPartialImplementation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionPartialImplementationCase
{
    public static void TestGoToDefinitionPartialImplementation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: goToDefinitionPartialImplementation_1.ts\nnamespace A {\n    export interface /*Part1Definition*/IA {\n        y: string;\n    }\n}\n// @Filename: goToDefinitionPartialImplementation_2.ts\nnamespace A {\n    export interface /*Part2Definition*/IA {\n        x: number;\n    }\n\n    var x: [|/*Part2Use*/IA|];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "Part2Use");
            }
            finally
            {
                done();
            }
        });
    }

}
