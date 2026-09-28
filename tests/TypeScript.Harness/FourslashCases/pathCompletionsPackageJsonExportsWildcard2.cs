// ported from: internal/fourslash/tests/pathCompletionsPackageJsonExportsWildcard2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonExportsWildcard2Case
{
    public static void TestPathCompletionsPackageJsonExportsWildcard2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /node_modules/salesforce-pageobjects/package.json\n{\n  \"name\": \"salesforce-pageobjects\",\n  \"version\": \"1.0.0\",\n  \"exports\": {\n    \"./*\": {\n      \"types\": \"./dist/*.d.ts\",\n      \"import\": \"./dist/*.mjs\",\n      \"default\": \"./dist/*.js\"\n    }\n  }\n}\n// @Filename: /node_modules/salesforce-pageobjects/dist/action/pageObjects/actionRenderer.d.ts\nexport const actionRenderer = 0;\n// @Filename: /index.mts\nimport { } from \"salesforce-pageobjects//**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "action", Kind = LSP.CompletionItemKind.Folder, Detail = "action" }] } });
                f.Insert(t, "action/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "pageObjects", Kind = LSP.CompletionItemKind.Folder, Detail = "pageObjects" }] } });
                f.Insert(t, "pageObjects/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "actionRenderer", Kind = LSP.CompletionItemKind.File, Detail = "actionRenderer.d.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
