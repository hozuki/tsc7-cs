// ported from: internal/fourslash/tests/goToDefinitionScriptImportServer_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionScriptImportServerCase
{
    public static void TestGoToDefinitionScriptImportServer(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/scriptThing.ts\n/*1d*/console.log(\"woooo side effects\")\n// @Filename: /home/src/workspaces/project/stylez.css\n/*2d*/div {\n  color: magenta;\n}\n// @Filename: /home/src/workspaces/project/moduleThing.ts\nimport [|/*1*/\"./scriptThing\"|];\nimport [|/*2*/\"./stylez.css\"|];\nimport [|/*3*/\"./foo.txt\"|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToDefinition(t, true, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
