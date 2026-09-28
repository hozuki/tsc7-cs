// ported from: internal/fourslash/tests/referencesInConfiguredProject_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesInConfiguredProjectCase
{
    public static void TestReferencesInConfiguredProject(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/referencesForGlobals_1.ts\nclass /*0*/globalClass {\n    public f() { }\n}\n// @Filename: /home/src/workspaces/project/referencesForGlobals_2.ts\nvar c = /*1*/globalClass();\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"files\": [\"referencesForGlobals_1.ts\", \"referencesForGlobals_2.ts\"], \"compilerOptions\": { \"lib\": [\"es5\"] } }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
