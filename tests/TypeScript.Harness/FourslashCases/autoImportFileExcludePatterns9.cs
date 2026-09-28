// ported from: internal/fourslash/tests/autoImportFileExcludePatterns9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportFileExcludePatterns9Case
{
    public static void TestAutoImportFileExcludePatterns9(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/vs/workbench/test.ts\nimport { Parts } from './parts';\nexport class /**/EditorParts implements Parts { }\n// @Filename: /src/vs/event/event.ts\nexport interface Event {\n\t(): string;\n}\n// @Filename: /src/vs/workbench/parts.ts\nimport { Event } from '../event/event';\nexport interface Parts {\n\treadonly options: Event;\n}\n// @Filename: /src/vs/workbench/workbench.ts\nimport { Event } from '../event/event';\nexport { Event };\n// @Filename: /src/vs/test.ts\nimport { Event } from './event/event';\nexport { Event };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Parts'", NewFileContent = "import { Event } from '../test';\nimport { Parts } from './parts';\nexport class EditorParts implements Parts {\n    options: Event;\n}", Index = 0, UserPreferences = new LSUP.UserPreferences { AutoImportFileExcludePatterns = ["src/vs/workbench/workbench*"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
