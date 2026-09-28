// ported from: internal/fourslash/tests/autoImportCompletion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCompletionCase
{
    public static void TestAutoImportCompletion1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport const someVar = 10;\n\n// @Filename: b.ts\nexport const anotherVar = 10;\n\n// @Filename: c.ts\nimport {someVar} from \"./a.ts\";\nsomeVar;\na/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["someVar", "anotherVar"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSFalse, IncludeCompletionsForImportStatements = TRI.TSFalse }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["anotherVar"] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportCompletion2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport const someVar = 10;\nexport const anotherVar = 10;\n\n// @Filename: c.ts\nimport {someVar} from \"./a.ts\";\nsomeVar;\na/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["someVar", "anotherVar"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportCompletion3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport const aa = \"asdf\";\nexport const someVar = 10;\nexport const bb = 10;\n\n// @Filename: c.ts\nimport { aa, someVar } from \"./a.ts\";\nsomeVar;\nb/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["bb"] } });
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

}
