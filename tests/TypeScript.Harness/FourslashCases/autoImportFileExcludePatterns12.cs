// ported from: internal/fourslash/tests/autoImportFileExcludePatterns12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportFileExcludePatterns12Case
{
    public static void TestAutoImportFileExcludePatterns12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/vs/test.ts\nimport { Parts } from './parts';\nexport class /**/Extended implements Parts {\n}\n// @Filename: /src/vs/parts.ts\nimport { Event } from '../thing';\nexport interface Parts {\n\treadonly options: Event;\n}\n// @Filename: /src/event/event.ts\nexport interface Event {\n\t(): string;\n}\n// @Filename: /src/thing.ts\nimport { Event } from '../event/event';\nexport { Event };\n// @Filename: /src/a.ts\nimport './thing'\ndeclare module './thing' {\n\tinterface Event {\n\t\tc: string;\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Parts'", NewFileContent = "import { Parts } from './parts';\nexport class Extended implements Parts {\n    options: Event;\n}", Index = 0, UserPreferences = new LSUP.UserPreferences { AutoImportFileExcludePatterns = ["src/thing.ts"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
