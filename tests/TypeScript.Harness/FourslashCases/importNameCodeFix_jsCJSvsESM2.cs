// ported from: internal/fourslash/tests/importNameCodeFix_jsCJSvsESM2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_jsCJSvsESM2Case
{
    public static void TestImportNameCodeFix_jsCJSvsESM2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: types/dep.d.ts\nexport declare class Dep {}\n// @Filename: index.js\nDep/**/\n// @Filename: util1.ts\nimport fs from 'fs';\n// @Filename: util2.js\nconst fs = require('fs');"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["const { Dep } = require(\"./types/dep\");\n\nDep"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
