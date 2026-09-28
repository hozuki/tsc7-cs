// ported from: internal/fourslash/tests/completionsPaths_pathMapping_nonTrailingWildcard1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsPaths_pathMapping_nonTrailingWildcard1Case
{
    public static void TestCompletionsPaths_pathMapping_nonTrailingWildcard1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/b.ts\nexport const x = 0;\n// @Filename: /src/dir/x.ts\n/export const x = 0;\n// @Filename: /src/a.ts\nimport {} from \"foo//*0*/\";\nimport {} from \"foo/dir//*1*/\"; // invalid\nimport {} from \"foo/[|_|]/*2*/\";\nimport {} from \"foo/_dir//*3*/\";\n// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \".\",\n        \"paths\": {\n            \"foo/_*/suffix\": [\"src/*.ts\"]\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "_a/suffix", Kind = LSP.CompletionItemKind.File, Detail = "_a/suffix.ts" }, new LSP.CompletionItem { Label = "_b/suffix", Kind = LSP.CompletionItemKind.File, Detail = "_b/suffix.ts" }, new LSP.CompletionItem { Label = "_dir", Detail = "_dir" }] } });
                f.VerifyCompletions(t, "1", null!);
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "_a/suffix", Kind = LSP.CompletionItemKind.File, Detail = "_a/suffix.ts", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { Range = f.Ranges()[0].LSRange, NewText = "_a/suffix" } } }, new LSP.CompletionItem { Label = "_b/suffix", Kind = LSP.CompletionItemKind.File, Detail = "_b/suffix.ts", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { Range = f.Ranges()[0].LSRange, NewText = "_b/suffix" } } }, new LSP.CompletionItem { Label = "_dir", Kind = LSP.CompletionItemKind.Folder, Detail = "_dir", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { Range = f.Ranges()[0].LSRange, NewText = "_dir" } } }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x/suffix", Kind = LSP.CompletionItemKind.File, Detail = "x/suffix.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
