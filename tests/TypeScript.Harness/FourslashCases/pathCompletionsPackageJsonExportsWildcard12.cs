// ported from: internal/fourslash/tests/pathCompletionsPackageJsonExportsWildcard12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonExportsWildcard12Case
{
    public static void TestPathCompletionsPackageJsonExportsWildcard12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /node_modules/foo/package.json\n {\n   \"name\": \"foo\",\n   \"exports\": {\n     \"./bar/_*/suffix\": \"./dist/*.js\"\n   }\n }\n// @Filename: /node_modules/foo/dist/b.d.ts\nexport const x = 0;\n// @Filename: /node_modules/foo/dist/dir/x.d.ts\n/export const x = 0;\n// @Filename: /a.mts\nimport {} from \"foo/bar//*0*/\";\nimport {} from \"foo/bar/dir//*1*/\"; // invalid\nimport {} from \"foo/bar/[|_|]/*2*/\";\nimport {} from \"foo/bar/_dir//*3*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "_b/suffix", Kind = LSP.CompletionItemKind.File, Detail = "_b/suffix.d.ts" }, new LSP.CompletionItem { Label = "_dir", Kind = LSP.CompletionItemKind.Folder, Detail = "_dir" }] } });
                f.VerifyCompletions(t, "1", null!);
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "_b/suffix", Kind = LSP.CompletionItemKind.File, Detail = "_b/suffix.d.ts", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { Range = f.Ranges()[0].LSRange, NewText = "_b/suffix" } } }, new LSP.CompletionItem { Label = "_dir", Kind = LSP.CompletionItemKind.Folder, Detail = "_dir", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { Range = f.Ranges()[0].LSRange, NewText = "_dir" } } }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x/suffix", Kind = LSP.CompletionItemKind.File, Detail = "x/suffix.d.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
