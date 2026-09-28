// ported from: internal/fourslash/tests/getEditsForFileRename_subDir_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_subDirCase
{
    public static void TestGetEditsForFileRename_subDir(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/foo/a.ts\n\n// @Filename: /src/old.ts\nimport a from \"./foo/a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/old.ts", "/src/dir/new.ts", new Dictionary<string, string> { ["/src/dir/new.ts"] = "import a from \"../foo/a\";" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
