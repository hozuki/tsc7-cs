// ported from: internal/fourslash/tests/importNameCodeFix_require_addToExisting_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_require_addToExistingCase
{
    public static void TestImportNameCodeFix_require_addToExisting(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: blah.js\nexport default class Blah {}\nexport const Named1 = 0;\nexport const Named2 = 1;\n// @Filename: index.js\nvar path = require('path')\n  , { promisify } = require('util')\n  , { Named1 } = require('./blah')\n\nnew Blah"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.js");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Update import from \"./blah\"", NewFileContent = "var path = require('path')\n  , { promisify } = require('util')\n  , { Named1, default: Blah } = require('./blah')\n\nnew Blah", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
