// ported from: internal/fourslash/tests/completionImportModuleSpecifierEndingDts_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionImportModuleSpecifierEndingDtsCase
{
    public static void TestCompletionImportModuleSpecifierEndingDts(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename:test.d.ts\n export declare class Test {}\n//@Filename:module.ts\nimport { Test } from \".//**/\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "test.js", Detail = "test.js" }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("js") } });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "test", Detail = "test.d.ts" }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("index") } });
            }
            finally
            {
                done();
            }
        });
    }

}
