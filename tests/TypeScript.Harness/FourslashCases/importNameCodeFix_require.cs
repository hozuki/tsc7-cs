// ported from: internal/fourslash/tests/importNameCodeFix_require_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_requireCase
{
    public static void TestImportNameCodeFix_require(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: foo.js\nmodule.exports = function foo() {}\n// @Filename: utils.js\nfunction util1() {}\nfunction util2() {}\nmodule.exports = { util1, util2 };\n// @Filename: blah.js\nexport default class Blah {}\n// @Filename: index.js\nfoo();\nutil1();\nutil2();\nnew Blah;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.js");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "const { default: Blah } = require(\"./blah\");\nconst foo = require(\"./foo\");\nconst { util1, util2 } = require(\"./utils\");\n\nfoo();\nutil1();\nutil2();\nnew Blah;" });
            }
            finally
            {
                done();
            }
        });
    }

}
