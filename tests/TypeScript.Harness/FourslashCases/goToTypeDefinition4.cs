// ported from: internal/fourslash/tests/goToTypeDefinition4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinition4Case
{
    public static void TestGoToTypeDefinition4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @Filename: foo.ts\nexport type /*def0*/T = string;\nexport const /*def1*/T = \"\";\n// @Filename: bar.ts\nimport { T } from \"./foo\";\nlet x: [|/*reference*/T|];";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference");
                f.VerifyBaselineGoToDefinition(t, true, "reference");
            }
            finally
            {
                done();
            }
        });
    }

}
