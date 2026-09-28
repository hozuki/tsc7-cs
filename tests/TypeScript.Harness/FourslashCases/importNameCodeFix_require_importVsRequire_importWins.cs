// ported from: internal/fourslash/tests/importNameCodeFix_require_importVsRequire_importWins_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_require_importVsRequire_importWinsCase
{
    public static void TestImportNameCodeFix_require_importVsRequire_importWins(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: blah.js\nexport default class Blah {}\nexport const Named1 = 0;\nexport const Named2 = 1;\n// @Filename: addToExisting.js\nconst { Named2 } = require('./blah')\nimport { Named1 } from './blah'\n\nnew Blah\n// @Filename: newImport.js\nimport fs from 'fs';\nconst path = require('path');\n\nnew Blah"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "addToExisting.js");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Update import from \"./blah\"", NewFileContent = "const { Named2 } = require('./blah')\nimport Blah, { Named1 } from './blah'\n\nnew Blah", Index = 0 });
                f.GoToFile(t, "newImport.js");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add import from \"./blah\"", NewFileContent = "import fs from 'fs';\nimport Blah from './blah';\nconst path = require('path');\n\nnew Blah", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
