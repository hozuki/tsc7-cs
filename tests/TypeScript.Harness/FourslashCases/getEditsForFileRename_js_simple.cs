// ported from: internal/fourslash/tests/getEditsForFileRename_js_simple_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_js_simpleCase
{
    public static void TestGetEditsForFileRename_js_simple(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\nimport b from \"./b.js\";\n// @Filename: /b.js\nmodule.exports = 1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/b.js", "/c.js", new Dictionary<string, string> { ["/a.js"] = "import b from \"./c.js\";" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
