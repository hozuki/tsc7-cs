// ported from: internal/fourslash/tests/completionForStringLiteral_quotePreference8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteral_quotePreference8Case
{
    public static void TestCompletionForStringLiteral_quotePreference8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /a.ts\nexport const a = null;\n// @filename: /b.ts\nimport { a } from './a';\n\nconst foo = { '\"a name\\'s all good but it\\'s better with more\"': null };\nfoo[|./**/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "\"a name's all good but it's better with more\"", InsertText = "['\"a name\\'s all good but it\\'s better with more\"']", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "['\"a name\\'s all good but it\\'s better with more\"']", Range = f.Ranges()[0].LSRange } } }] }, UserPreferences = new LSUP.UserPreferences { QuotePreference = new LSUP.QuotePreference("auto") } });
            }
            finally
            {
                done();
            }
        });
    }

}
