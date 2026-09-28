// ported from: internal/fourslash/tests/goToSource5_sameAsGoToDef1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource5_sameAsGoToDef1Case
{
    public static void TestGoToSource5_sameAsGoToDef1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/a.ts\nexport const /*end*/a = 'a';\n// @Filename: /home/src/workspaces/project/a.d.ts\nexport declare const a: string;\n// @Filename: /home/src/workspaces/project/a.js\nexport const a = 'a';\n// @Filename: /home/src/workspaces/project/b.ts\nimport { a } from './a';\n[|a/*start*/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "start");
                f.VerifyBaselineGoToDefinition(t, true, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
