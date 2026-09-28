// ported from: internal/fourslash/tests/goToSource11_propertyOfAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource11_propertyOfAliasCase
{
    public static void TestGoToSource11_propertyOfAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @lib: es5\n// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/a.js\nexport const a = { /*end*/a: 'a' };\n// @Filename: /home/src/workspaces/project/a.d.ts\nexport declare const a: { a: string };\n// @Filename: /home/src/workspaces/project/b.ts\nimport { a } from './a';\na.[|a/*start*/|]";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
