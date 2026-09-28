// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsWildcard3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsWildcard3Case
{
    public static void TestPathCompletionsPackageJsonImportsWildcard3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /package.json\n{\n  \"types\": \"index.d.ts\",\n  \"imports\": {\n    \"#component-*\": {\n      \"types@>=4.3.5\": \"types/components/*.d.ts\"\n    }\n  }\n}\n// @Filename: /nope.d.ts\nexport const nope = 0;\n// @Filename: /types/components/index.d.ts\nexport const index = 0;\n// @Filename: /types/components/blah.d.ts\nexport const blah = 0;\n// @Filename: /types/components/subfolder/one.d.ts\nexport const one = 0;\n// @Filename: /a.ts\nimport { } from \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "#component-blah", Kind = LSP.CompletionItemKind.File, Detail = "#component-blah.d.ts" }, new LSP.CompletionItem { Label = "#component-index", Kind = LSP.CompletionItemKind.File, Detail = "#component-index.d.ts" }, new LSP.CompletionItem { Label = "#component-subfolder", Kind = LSP.CompletionItemKind.Folder, Detail = "#component-subfolder" }] } });
                f.Insert(t, "#component-subfolder/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "one", Kind = LSP.CompletionItemKind.File, Detail = "one.d.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
