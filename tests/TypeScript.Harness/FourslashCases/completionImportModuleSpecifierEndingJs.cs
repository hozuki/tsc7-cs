// ported from: internal/fourslash/tests/completionImportModuleSpecifierEndingJs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionImportModuleSpecifierEndingJsCase
{
    public static void TestCompletionImportModuleSpecifierEndingJs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@allowJs: true\n//@Filename:test.js\nexport function f(){\n    return 1\n}\n//@Filename:module.js\nimport { f } from \".//**/\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "test.js", Detail = "test.js" }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("js") } });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "test", Detail = "test.js" }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("index") } });
            }
            finally
            {
                done();
            }
        });
    }

}
