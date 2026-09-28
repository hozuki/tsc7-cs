// ported from: internal/fourslash/tests/goToSourceDefinitionAliasedImportWithPrecedingExports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionAliasedImportWithPrecedingExportsCase
{
    public static void TestGoToSourceAliasedImportWithPrecedingExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function unrelated(): void;\nexport declare function original(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function unrelated() {}\nexport function /*target*/original() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { original as /*aliasedImport*/renamed } from \"pkg\";\nrenamed();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "aliasedImport");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceReExportAliasWithPrecedingExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function unrelated(): void;\nexport declare function original(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function unrelated() {}\nexport function /*target*/original() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/reexport.ts\nexport { original as /*reExportAlias*/renamed } from \"pkg\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "reExportAlias");
            }
            finally
            {
                done();
            }
        });
    }

}
