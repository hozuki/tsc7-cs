// ported from: internal/fourslash/tests/importNameCodeFix_jsCJSvsESM3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_jsCJSvsESM3Case
{
    public static void TestImportNameCodeFix_jsCJSvsESM3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: types/dep.d.ts\nexport declare class Dep {}\n// @Filename: index.js\nimport fs from 'fs';\nconst path = require('path');\n\nDep/**/\n// @Filename: util2.js\nexport {};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import fs from 'fs';\nimport { Dep } from './types/dep';\nconst path = require('path');\n\nDep"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
