// ported from: internal/fourslash/tests/getEditsForFileRename_preferences_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_preferencesCase
{
    public static void TestGetEditsForFileRename_preferences(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /dir/a.ts\nexport const a = 0;\n// @Filename: /dir/b.ts\nimport {} from \"dir/a\";\nimport {} from 'dir/a';\n// @Filename: /tsconfig.json\n{\"compilerOptions\":{\"paths\":{\"*\":[\"*\"]}}}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/dir/a.ts", "/dir/a1.ts", new Dictionary<string, string> { ["/dir/b.ts"] = "import {} from \"dir/a1\";\nimport {} from 'dir/a1';" }, new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative"), QuotePreference = new LSUP.QuotePreference("single") });
            }
            finally
            {
                done();
            }
        });
    }

}
