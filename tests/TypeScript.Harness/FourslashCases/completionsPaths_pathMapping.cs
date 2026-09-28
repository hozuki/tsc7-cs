// ported from: internal/fourslash/tests/completionsPaths_pathMapping_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsPaths_pathMappingCase
{
    public static void TestCompletionsPaths_pathMapping(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/b.ts\nexport const x = 0;\n// @Filename: /src/dir/x.ts\n/export const x = 0;\n// @Filename: /src/a.ts\nimport {} from \"foo//*0*/\";\nimport {} from \"foo/dir//*1*/\";\n// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \".\",\n        \"paths\": {\n            \"foo/*\": [\"src/*\"]\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", Kind = LSP.CompletionItemKind.File, Detail = "a.ts" }, new LSP.CompletionItem { Label = "b", Kind = LSP.CompletionItemKind.File, Detail = "b.ts" }, new LSP.CompletionItem { Label = "dir", Kind = LSP.CompletionItemKind.Folder, Detail = "dir" }] } });
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", Kind = LSP.CompletionItemKind.File, Detail = "x.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
