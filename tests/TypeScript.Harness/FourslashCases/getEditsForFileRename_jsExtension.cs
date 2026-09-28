// ported from: internal/fourslash/tests/getEditsForFileRename_jsExtension_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_jsExtensionCase
{
    public static void TestGetEditsForFileRename_jsExtension(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /src/a.js\nexport const a = 0;\n// @Filename: /b.js\nimport { a } from \"./src/a.js\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/b.js", "/src/b.js", new Dictionary<string, string> { ["/src/b.js"] = "import { a } from \"./a.js\";" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
