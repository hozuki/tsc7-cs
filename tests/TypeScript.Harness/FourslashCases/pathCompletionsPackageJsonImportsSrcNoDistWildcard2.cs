// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsSrcNoDistWildcard2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsSrcNoDistWildcard2Case
{
    public static void TestPathCompletionsPackageJsonImportsSrcNoDistWildcard2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"rootDir\": \"src\",\n    \"outDir\": \"dist\"\n  }\n}\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"name\": \"salesforce-pageobjects\",\n  \"version\": \"1.0.0\",\n  \"imports\": {\n    \"#*\": {\n      \"types\": \"./dist/*.d.ts\",\n      \"import\": \"./dist/*.mjs\",\n      \"default\": \"./dist/*.js\"\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/src/action/pageObjects/actionRenderer.ts\nexport const actionRenderer = 0;\n// @Filename: /home/src/workspaces/project/src/index.mts\nimport { } from \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "#action", Kind = LSP.CompletionItemKind.Folder, Detail = "#action" }] } });
                f.Insert(t, "#action/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "pageObjects", Kind = LSP.CompletionItemKind.Folder, Detail = "pageObjects" }] } });
                f.Insert(t, "pageObjects/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "actionRenderer", Kind = LSP.CompletionItemKind.File, Detail = "actionRenderer.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
