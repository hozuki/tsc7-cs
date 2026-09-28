// ported from: internal/fourslash/tests/getEditsForFileRename_ambientModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_ambientModuleCase
{
    public static void TestGetEditsForFileRename_ambientModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{}\n// @Filename: /sub/types.d.ts\n// @Symlink: /node_modules/sub/types.d.ts\ndeclare module \"sub\" {\n    declare export const abc: number\n}\n// @Filename: /sub/package.json\n// @Symlink: /node_modules/sub/package.json\n{ \"types\": \"types.d.ts\" }\n// @Filename: /a.ts\nimport { abc } from \"sub\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/a.ts", "/b.ts", new Dictionary<string, string> {  }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
