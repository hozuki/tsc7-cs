// ported from: internal/fourslash/tests/importModuleSpecifierEnding_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportModuleSpecifierEndingCase
{
    public static void TestImportModuleSpecifierEndingAuto(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/helper/index.ts\nexport const helperFunc = () => {};\n// @Filename: /project/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierEndingPreferenceAuto) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportModuleSpecifierEndingMinimal(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/helper/index.ts\nexport const helperFunc = () => {};\n// @Filename: /project/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierEndingPreferenceMinimal) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportModuleSpecifierEndingIndex(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/helper/index.ts\nexport const helperFunc = () => {};\n// @Filename: /project/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierEndingPreferenceIndex) });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["helperFunc"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportModuleSpecifierEndingJs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/helper/index.ts\nexport const helperFunc = () => {};\n// @Filename: /project/index.ts\nhelper/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue, ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)(MS.ModuleSpecifiers.ImportModuleSpecifierEndingPreferenceJs) });
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
