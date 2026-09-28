// ported from: internal/fourslash/tests/importNameCodeFix_require_importVsRequire_moduleTarget_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_require_importVsRequire_moduleTargetCase
{
    public static void TestImportNameCodeFix_require_importVsRequire_moduleTarget(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @module: es2015\n// @Filename: a.js\nexport const x = 0;\n// @Filename: index.js\nx"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.js");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add import from \"./a\"", NewFileContent = "import { x } from \"./a\";\n\nx", Index = 0 });
                f.GoToPosition(t, 0);
                f.InsertLine(t, "const fs = require('fs');\n");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add import from \"./a\"", NewFileContent = "const fs = require('fs');\nconst { x } = require('./a');\n\nx", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
