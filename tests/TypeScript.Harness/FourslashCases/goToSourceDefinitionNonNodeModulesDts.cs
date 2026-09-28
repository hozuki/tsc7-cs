// ported from: internal/fourslash/tests/goToSourceDefinitionNonNodeModulesDts_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionNonNodeModulesDtsCase
{
    public static void TestGoToSourceFindImplementationNonNodeModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @declaration: true\n// @Filename: /home/src/workspaces/project/lib/helper.d.ts\nexport declare function helper(): string;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*usage*/helper } from \"./lib/helper\";\nhelper();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "usage");
            }
            finally
            {
                done();
            }
        });
    }

}
