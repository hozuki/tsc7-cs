// ported from: internal/fourslash/tests/goToSourceDefinitionScopedPackage_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionScopedPackageCase
{
    public static void TestGoToSourceScopedPackage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/@myscope/mylib/package.json\n{ \"name\": \"@myscope/mylib\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/@myscope/mylib/index.d.ts\nexport declare function scopedHelper(): string;\n// @Filename: /home/src/workspaces/project/node_modules/@myscope/mylib/index.js\nexport function /*target*/scopedHelper() { return \"scoped\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { /*importName*/scopedHelper } from \"@myscope/mylib\";\nscopedHelper/*usage*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "importName", "usage");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourceScopedAtTypesPackage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/@types/myns__mylib/package.json\n{ \"name\": \"@types/myns__mylib\", \"version\": \"1.0.0\" }\n// @Filename: /home/src/workspaces/project/node_modules/@types/myns__mylib/index.d.ts\nexport declare function nsHelper(): number;\n// @Filename: /home/src/workspaces/project/node_modules/@myns/mylib/package.json\n{ \"name\": \"@myns/mylib\", \"version\": \"1.0.0\", \"main\": \"./index.js\" }\n// @Filename: /home/src/workspaces/project/node_modules/@myns/mylib/index.js\nexport function /*target*/nsHelper() { return 42; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { nsHelper } from \"@myns/mylib\";\nnsHelper/*usage*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "usage");
            }
            finally
            {
                done();
            }
        });
    }

}
