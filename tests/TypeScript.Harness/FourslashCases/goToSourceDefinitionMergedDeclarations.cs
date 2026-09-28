// ported from: internal/fourslash/tests/goToSourceDefinitionMergedDeclarations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionMergedDeclarationsCase
{
    public static void TestGoToSourceMergedDeclarationDedup(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare class /*dtsClass*/Util {\n    run(): void;\n}\nexport declare namespace Util {\n    export const version: string;\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport class /*targetUtil*/Util {\n    run() {}\n}\nUtil.version = \"1.0\";\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importUtil*/Util } from \"pkg\";\nconst u: /*typeRef*/Util = new Util();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importUtil", "typeRef");
            }
            finally
            {
                done();
            }
        });
    }

}
