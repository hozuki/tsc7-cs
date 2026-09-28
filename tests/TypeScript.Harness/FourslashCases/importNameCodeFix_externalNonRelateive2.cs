// ported from: internal/fourslash/tests/importNameCodeFix_externalNonRelateive2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_externalNonRelateive2Case
{
    public static void TestImportNameCodeFix_externalNonRelateive2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/apps/app1/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"commonjs\",\n    \"lib\": [\"es5\"],\n    \"paths\": {\n      \"shared/*\": [\"../../shared/*\"]\n    }\n  },\n  \"include\": [\"src\", \"../../shared\"]\n}\n// @Filename: /home/src/workspaces/project/apps/app1/src/index.ts\nshared/*internal2external*/\n// @Filename: /home/src/workspaces/project/apps/app1/src/app.ts\nutils/*internal2internal*/\n// @Filename: /home/src/workspaces/project/apps/app1/src/utils.ts\nexport const utils = 0;\n// @Filename: /home/src/workspaces/project/shared/constants.ts\nexport const shared = 0;\n// @Filename: /home/src/workspaces/project/shared/data.ts\nshared/*external2external*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                var opts839 = f.GetOptions();
                opts839!.FormatCodeSettings!.NewLineCharacter = "\n";
                f.Configure(t, opts839);
                f.GoToMarker(t, "internal2external");
                f.VerifyImportFixAtPosition(t, ["import { shared } from \"shared/constants\";\n\nshared"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("project-relative") });
                f.GoToMarker(t, "internal2internal");
                f.VerifyImportFixAtPosition(t, ["import { utils } from \"./utils\";\n\nutils"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("project-relative") });
                f.GoToMarker(t, "external2external");
                f.VerifyImportFixAtPosition(t, ["import { shared } from \"./constants\";\n\nshared"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("project-relative") });
            }
            finally
            {
                done();
            }
        });
    }

}
