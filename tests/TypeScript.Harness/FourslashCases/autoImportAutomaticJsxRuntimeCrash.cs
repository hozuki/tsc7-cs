// ported from: internal/fourslash/tests/autoImportAutomaticJsxRuntimeCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportAutomaticJsxRuntimeCrashCase
{
    public static void TestAutoImportAutomaticJsxRuntimeCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"types\": \"index.tsx\" }\n\n// @Filename: /project/node_modules/pkg/index.tsx\n/** @jsxRuntime automatic */\nconst container = { Widget: { value: <div /> } satisfies {} };\nexport default container.Widget;\n\n// @Filename: /project/package.json\n{ \"dependencies\": { \"pkg\": \"*\" } }\n\n// @Filename: /project/tsconfig.json\n{ \"compilerOptions\": { \"jsx\": \"react-jsx\" } }\n\n// @Filename: /project/index.ts\nWidg/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["Widget"] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportAutomaticJsxRuntimeProjectReferenceCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /packages/lib/package.json\n{ \"name\": \"lib\", \"types\": \"out/index.d.ts\" }\n\n// @Filename: /packages/lib/tsconfig.json\n{ \"compilerOptions\": { \"composite\": true, \"jsx\": \"react-jsx\", \"outDir\": \"out\" } }\n\n// @Filename: /packages/lib/index.tsx\n/** @jsxRuntime automatic */\nconst container = { Widget: { value: <div /> } satisfies {} };\nexport default container.Widget;\n\n// @Filename: /packages/app/package.json\n{ \"dependencies\": { \"lib\": \"*\" } }\n\n// @Filename: /packages/app/tsconfig.json\n{ \"references\": [{ \"path\": \"../lib\" }] }\n\n// @Filename: /packages/app/index.ts\nWidg/**/\n\n// @link: /packages/lib -> /packages/app/node_modules/lib\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["Widget"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
