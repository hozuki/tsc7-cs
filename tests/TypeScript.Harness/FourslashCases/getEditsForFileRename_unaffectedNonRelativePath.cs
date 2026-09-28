// ported from: internal/fourslash/tests/getEditsForFileRename_unaffectedNonRelativePath_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_unaffectedNonRelativePathCase
{
    public static void TestGetEditsForFileRename_unaffectedNonRelativePath(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /sub/a.ts\nexport const a = 1;\n// @Filename: /sub/b.ts\nimport { a } from \"sub/a\";\n// @Filename: /tsconfig.json\n{\"compilerOptions\":{\"paths\":{\"*\":[\"*\"]}}}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/sub/b.ts", "/sub/c/d.ts", new Dictionary<string, string> {  }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
