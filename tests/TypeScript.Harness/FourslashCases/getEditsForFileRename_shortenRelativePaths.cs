// ported from: internal/fourslash/tests/getEditsForFileRename_shortenRelativePaths_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_shortenRelativePathsCase
{
    public static void TestGetEditsForFileRename_shortenRelativePaths(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/foo/x.ts\n\n// @Filename: /src/old.ts\nimport { x } from \"./foo/x\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/old.ts", "/src/foo/new.ts", new Dictionary<string, string> { ["/src/foo/new.ts"] = "import { x } from \"./x\";" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
