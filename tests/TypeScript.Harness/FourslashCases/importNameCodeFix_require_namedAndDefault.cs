// ported from: internal/fourslash/tests/importNameCodeFix_require_namedAndDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_require_namedAndDefaultCase
{
    public static void TestImportNameCodeFix_require_namedAndDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: blah.ts\nexport default class Blah {}\nexport const Named1 = 0;\nexport const Named2 = 1;\n// @Filename: index.js\nNamed1 + Named2;\nnew Blah;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.js");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "const { default: Blah, Named1, Named2 } = require(\"./blah\");\n\nNamed1 + Named2;\nnew Blah;" });
            }
            finally
            {
                done();
            }
        });
    }

}
