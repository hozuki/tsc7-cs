// ported from: internal/fourslash/tests/getEditsForFileRename_nodeModuleDirectoryCase_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_nodeModuleDirectoryCaseCase
{
    public static void TestGetEditsForFileRename_nodeModuleDirectoryCase(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a/b/file1.ts\nimport { foo } from \"foo\";\n// @Filename: /a/node_modules/foo/index.d.ts\nexport const foo = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/a/b", "/a/B", new Dictionary<string, string> {  }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
