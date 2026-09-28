// ported from: internal/fourslash/tests/goToSourceDefinitionPropertyAccess_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionPropertyAccessCase
{
    public static void TestGoToSourceMappedTypePropertyWithMatch(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare const obj: { a: number; b: number };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const obj = { /*targetA*/a: 1, /*targetB*/b: 2 };\n// @Filename: /home/src/workspaces/project/index.ts\nimport { obj } from \"pkg\";\nobj./*propA*/a;\nobj./*propB*/b;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "propA", "propB");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceNamespaceImportProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function helper(): void;\nexport declare const value: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*targetHelper*/helper() {}\nexport const /*targetValue*/value = 42;\n// @Filename: /home/src/workspaces/project/index.ts\nimport * as pkg from \"pkg\";\npkg./*helperAccess*/helper();\npkg./*valueAccess*/value;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "helperAccess", "valueAccess");
            }
            finally
            {
                done();
            }
        });
    }

}
