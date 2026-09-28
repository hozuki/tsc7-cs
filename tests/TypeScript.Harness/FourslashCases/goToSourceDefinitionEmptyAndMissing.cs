// ported from: internal/fourslash/tests/goToSourceDefinitionEmptyAndMissing_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionEmptyAndMissingCase
{
    public static void TestGoToSourceDefinitionEmptyJsFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function foo(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\n// @Filename: /home/src/workspaces/project/index.ts\nimport { foo } from /*specifier*/\"pkg\";\nfoo();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "specifier");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceDefaultImportNoDefaultInJs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport default function create(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\n/*targetEntry*/function internalCreate() { return {}; }\nmodule.exports = { create: internalCreate };\n// @Filename: /home/src/workspaces/project/index.ts\nimport /*importDefault*/create from \"pkg\";\ncreate();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importDefault");
            }
            finally
            {
                done();
            }
        });
    }

}
