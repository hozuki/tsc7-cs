// ported from: internal/fourslash/tests/importModuleSpecifierPreference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportModuleSpecifierPreferenceCase
{
    public static void TestImportModuleSpecifierPreferenceShortest(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/src/utils/helper.ts\nexport const helperFunc = () => {};\n// @Filename: /project/src/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierPreferenceShortest) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportModuleSpecifierPreferenceProjectRelative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/src/utils/helper.ts\nexport const helperFunc = () => {};\n// @Filename: /project/tests/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierPreferenceProjectRelative) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportModuleSpecifierPreferenceRelative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/src/utils/helper.ts\nexport const helperFunc = () => {};\n// @Filename: /project/src/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierPreferenceRelative) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportModuleSpecifierPreferenceProjectRelativeWithPaths(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"paths\": {\n      \"@app/*\": [\"./src/app/*\"],\n      \"@utils/*\": [\"./src/utils/*\"],\n    }\n  }\n}\n// @Filename: /project/src/utils/helper.ts\nexport const helperFunc = () => {};\n// @Filename: /project/src/app/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierPreferenceProjectRelative) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportModuleSpecifierPreferenceNonRelative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"paths\": {\n      \"@app/*\": [\"./src/app/*\"],\n      \"@utils/*\": [\"./src/utils/*\"],\n    }\n  }\n}\n// @Filename: /project/src/utils/helper.ts\nexport const helperFunc = () => {};\n// @Filename: /project/src/app/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierPreferenceNonRelative) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

}
