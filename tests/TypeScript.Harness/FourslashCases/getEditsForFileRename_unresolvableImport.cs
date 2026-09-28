// ported from: internal/fourslash/tests/getEditsForFileRename_unresolvableImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_unresolvableImportCase
{
    public static void TestGetEditsForFileRename_unresolvableImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"allowJs\": true,\n    \"paths\": {\n      \"*\": [\"./next/src/*\"],\n      \"@app\": [\"./modules/@app/*\"],\n      \"@app/*\": [\"./modules/@app/*\"],\n      \"@local\": [\"./modules/@local/*\"],\n      \"@local/*\": [\"./modules/@local/*\"]\n    }\n  }\n}\n// @Filename: /modules/@app/something/index.js\nimport \"@local/some-other-import\";\n// @Filename: /modules/@local/index.js\nimport \"@local/some-other-import\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/modules/@app/something", "/modules/@app/something-2", new Dictionary<string, string> {  }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
