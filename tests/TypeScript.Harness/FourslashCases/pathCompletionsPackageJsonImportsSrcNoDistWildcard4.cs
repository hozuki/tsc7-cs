// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsSrcNoDistWildcard4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsSrcNoDistWildcard4Case
{
    public static void TestPathCompletionsPackageJsonImportsSrcNoDistWildcard4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"rootDir\": \"src\",\n    \"outDir\": \"dist\"\n  }\n}\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"types\": \"index.d.ts\",\n  \"imports\": {\n    \"#*\": \"dist/*\",\n    \"#foo/*\": \"dist/*\",\n    \"#bar/*\": \"dist/*\",\n    \"#exact-match\": \"dist/index.d.ts\"\n  }\n}\n// @Filename: /home/src/workspaces/project/nope.ts\nexport const nope = 0;\n// @Filename: /home/src/workspaces/project/src/index.ts\nexport const index = 0;\n// @Filename: /home/src/workspaces/project/src/blah.ts\nexport const blah = 0;\n// @Filename: /home/src/workspaces/project/src/foo/onlyInFooFolder.ts\nexport const foo = 0;\n// @Filename: /home/src/workspaces/project/src/subfolder/one.ts\nexport const one = 0;\n// @Filename: /home/src/workspaces/project/src/a.mts\nimport { } from \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "#a.mjs", Kind = LSP.CompletionItemKind.File, Detail = "#a.mjs" }, new LSP.CompletionItem { Label = "#blah.js", Kind = LSP.CompletionItemKind.File, Detail = "#blah.js" }, new LSP.CompletionItem { Label = "#index.js", Kind = LSP.CompletionItemKind.File, Detail = "#index.js" }, new LSP.CompletionItem { Label = "#foo", Kind = LSP.CompletionItemKind.Folder, Detail = "#foo" }, new LSP.CompletionItem { Label = "#subfolder", Kind = LSP.CompletionItemKind.Folder, Detail = "#subfolder" }, new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Folder, Detail = "#bar" }, new LSP.CompletionItem { Label = "#exact-match", Kind = LSP.CompletionItemKind.File, Detail = "#exact-match.d.ts" }] } });
                f.Insert(t, "#foo/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "a.mjs", Kind = LSP.CompletionItemKind.File, Detail = "a.mjs" }, new LSP.CompletionItem { Label = "blah.js", Kind = LSP.CompletionItemKind.File, Detail = "blah.js" }, new LSP.CompletionItem { Label = "index.js", Kind = LSP.CompletionItemKind.File, Detail = "index.js" }, new LSP.CompletionItem { Label = "foo", Kind = LSP.CompletionItemKind.Folder, Detail = "foo" }, new LSP.CompletionItem { Label = "subfolder", Kind = LSP.CompletionItemKind.Folder, Detail = "subfolder" }] } });
                f.Insert(t, "foo/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "onlyInFooFolder.js", Kind = LSP.CompletionItemKind.File, Detail = "onlyInFooFolder.js" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
