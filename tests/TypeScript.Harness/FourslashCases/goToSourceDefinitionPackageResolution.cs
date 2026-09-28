// ported from: internal/fourslash/tests/goToSourceDefinitionPackageResolution_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionPackageResolutionCase
{
    public static void TestGoToSourceAtTypesPackage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/@types/foo/package.json\n{ \"name\": \"@types/foo\", \"version\": \"1.0.0\" }\n// @Filename: /home/src/workspaces/project/node_modules/@types/foo/index.d.ts\nexport declare function bar(): string;\n// @Filename: /home/src/workspaces/project/node_modules/foo/package.json\n{ \"name\": \"foo\", \"version\": \"1.0.0\", \"main\": \"./index.js\" }\n// @Filename: /home/src/workspaces/project/node_modules/foo/index.js\nexport function /*target*/bar() { return \"hello\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { bar } from \"foo\";\nbar/*usage*/();"u8);
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

    public static void TestGoToSourcePackageIndexDts(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./lib/index.js\", \"types\": \"./lib/index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/lib/index.d.ts\nexport declare function greet(): string;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/lib/index.js\nexport function /*target*/greet() { return \"hi\"; }\n// @Filename: /home/src/workspaces/project/index.ts\nimport { greet } from \"pkg\";\ngreet/*usage*/();"u8);
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

    public static void TestGoToSourcePackageRootThenSubpath(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function work(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*target*/work() {}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { work } from \"pkg\";\nwork/*usage*/();"u8);
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

    public static void TestGoToSourcePackageRootFallsBackToSubpath(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare function work(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport function /*target*/work() {}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { work } from \"pkg\";\nwork/*usage*/();"u8);
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

    public static void TestGoToSourceSubpathNotIndex(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"types\": \"./lib/utils.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/lib/utils.d.ts\nexport declare function util(): void;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/lib/utils.js\nexport function /*target*/util() {}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { util } from \"pkg\";\nutil/*usage*/();"u8);
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
