// ported from: internal/fourslash/tests/referencesInStringLiteralValueWithMultipleProjects_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesInStringLiteralValueWithMultipleProjectsCase
{
    public static void TestReferencesInStringLiteralValueWithMultipleProjects(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/a/tsconfig.json\n{ \"files\": [\"a.ts\"], \"compilerOptions\": { \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/a/a.ts\n/// <reference path=\"../b/b.ts\" />\nconst str: string = \"hello/*1*/\";\n// @Filename: /home/src/workspaces/project/b/tsconfig.json\n{ \"files\": [\"b.ts\"], \"compilerOptions\": { \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/b/b.ts\nconst str2: string = \"hello/*2*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineFindAllReferences(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
