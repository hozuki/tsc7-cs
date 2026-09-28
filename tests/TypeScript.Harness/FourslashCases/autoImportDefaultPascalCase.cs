// ported from: internal/fourslash/tests/autoImportDefaultPascalCase_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportDefaultPascalCaseCase
{
    public static void TestAutoImportDefaultPascalCase(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @module: esnext\n// @moduleResolution: bundler\n\n// @Filename: /src/components/ChargerHeader.tsx\nexport default function ChargerHeader() {\n  return null;\n}\n\n// @Filename: /src/screens/SomeScreen.tsx\nexport function SomeScreen() {\n  return <ChargerHeader/*1*/\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["ChargerHeader"] } });
                f.BaselineAutoImportsCompletions(t, ["1"]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportDefaultPascalCaseAnonymous(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @module: esnext\n// @moduleResolution: bundler\n\n// @Filename: /src/components/ChargerHeader.tsx\nexport default function() {\n  return null;\n}\n\n// @Filename: /src/screens/SomeScreen.tsx\nexport function SomeScreen() {\n  return <ChargerHeader/*1*/\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["ChargerHeader"] } });
                f.BaselineAutoImportsCompletions(t, ["1"]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportDefaultPascalCaseCaseInsensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @module: esnext\n// @moduleResolution: bundler\n// @useCaseSensitiveFileNames: false\n\n// @Filename: /src/components/ChargerHeader.tsx\nexport default function ChargerHeader() {\n  return null;\n}\n\n// @Filename: /src/screens/SomeScreen.tsx\nexport function SomeScreen() {\n  return <ChargerHeader/*1*/\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["ChargerHeader"] } });
                f.BaselineAutoImportsCompletions(t, ["1"]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportDefaultPascalCaseAnonymousCaseInsensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @module: esnext\n// @moduleResolution: bundler\n// @useCaseSensitiveFileNames: false\n\n// @Filename: /src/components/ChargerHeader.tsx\nexport default function() {\n  return null;\n}\n\n// @Filename: /src/screens/SomeScreen.tsx\nexport function SomeScreen() {\n  return <ChargerHeader/*1*/\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["ChargerHeader"] } });
                f.BaselineAutoImportsCompletions(t, ["1"]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportDefaultPascalCaseReexportCaseInsensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @module: esnext\n// @moduleResolution: bundler\n// @useCaseSensitiveFileNames: false\n\n// @Filename: /src/components/ChargerHeader.tsx\nexport default function() {\n  return null;\n}\n\n// @Filename: /src/components/index.ts\nexport { default } from \"./ChargerHeader\";\n\n// @Filename: /src/screens/SomeScreen.tsx\nexport function SomeScreen() {\n  return <ChargerHeader/*1*/\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["ChargerHeader"] } });
                f.BaselineAutoImportsCompletions(t, ["1"]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportDefaultPascalCaseAliasCaseInsensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @module: esnext\n// @moduleResolution: bundler\n// @useCaseSensitiveFileNames: false\n\n// @Filename: /src/components/ChargerHeader.tsx\nfunction ChargerHeader() {\n  return null;\n}\nexport default ChargerHeader;\n\n// @Filename: /src/screens/SomeScreen.tsx\nexport function SomeScreen() {\n  return <ChargerHeader/*1*/\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["ChargerHeader"] } });
                f.BaselineAutoImportsCompletions(t, ["1"]);
            }
            finally
            {
                done();
            }
        });
    }

}
