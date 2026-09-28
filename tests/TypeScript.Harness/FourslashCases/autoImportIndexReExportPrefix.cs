// ported from: internal/fourslash/tests/autoImportIndexReExportPrefix_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportIndexReExportPrefixCase
{
    public static void TestAutoImportIndexReExportPrefix(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: nodenext\n// @Filename: /package.json\n{ \"type\": \"module\" }\n// @Filename: /utils/sum/index.ts\nexport { sum } from \"./sum.js\";\n// @Filename: /utils/sum/sum.ts\nexport const sum = 0;\n// @Filename: /utils/sumAB.ts\nsum/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["./sum/index.js", "./sum/sum.js"], new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("js") });
            }
            finally
            {
                done();
            }
        });
    }

}
