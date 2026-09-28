// ported from: internal/fourslash/tests/keywordShadowsAutoImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class KeywordShadowsAutoImportCase
{
    public static void TestKeywordShadowsAutoImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /mod.ts\nconst value = 1;\nexport { value as function }\n\n// @Filename: /index.ts\nfunction/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsForModuleExports = TRI.TSTrue, IncludeCompletionsForImportStatements = TRI.TSTrue }, IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "function", Kind = LSP.CompletionItemKind.Keyword, SortText = LS.SortTextGlobalsOrKeywords }], Excludes = ["function"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
