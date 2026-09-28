// ported from: internal/fourslash/tests/goToSource1_localJsBesideDts_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource1_localJsBesideDtsCase
{
    public static void TestGoToSource1_localJsBesideDts(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/a.js\nexport const /*end*/a = \"a\";\n// @Filename: /home/src/workspaces/project/a.d.ts\nexport declare const a: string;\n// @Filename: /home/src/workspaces/project/index.ts\nimport { a } from [|\"./a\"/*moduleSpecifier*/|];\n[|a/*identifier*/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "identifier", "moduleSpecifier");
            }
            finally
            {
                done();
            }
        });
    }

}
