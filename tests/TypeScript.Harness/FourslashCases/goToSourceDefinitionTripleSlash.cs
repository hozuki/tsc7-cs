// ported from: internal/fourslash/tests/goToSourceDefinitionTripleSlash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionTripleSlashCase
{
    public static void TestGoToSourceReferenceTypesToJS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/@types/foo/package.json\n{ \"name\": \"@types/foo\", \"version\": \"1.0.0\" }\n// @Filename: /home/src/workspaces/project/node_modules/@types/foo/index.d.ts\nexport declare function bar(): string;\n// @Filename: /home/src/workspaces/project/node_modules/foo/package.json\n{ \"name\": \"foo\", \"version\": \"1.0.0\", \"main\": \"./index.js\" }\n// @Filename: /home/src/workspaces/project/node_modules/foo/index.js\nexport function /*target*/bar() { return \"hello\"; }\n// @Filename: /home/src/workspaces/project/index.ts\n/// <reference types=\"[|foo/*refTypes*/|]\" />\nimport { bar } from \"foo\";\nbar();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "refTypes");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceReferencePathToDts(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\n/// <reference path=\"./lib.d.ts\" />\nexport declare function main(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/lib.d.ts\nexport declare function helper(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/lib.js\nexport function /*target*/helper() { return \"ok\"; }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function main() {}\n// @Filename: /home/src/workspaces/project/index.ts\n/// <reference path=\"./node_modules/pkg/[|lib.d.ts/*refPath*/|]\" />\ndeclare function helper(): string;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "refPath");
            }
            finally
            {
                done();
            }
        });
    }

}
