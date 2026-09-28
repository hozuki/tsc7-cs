// ported from: internal/fourslash/tests/completionsPaths_pathMapping_parentDirectory_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsPaths_pathMapping_parentDirectoryCase
{
    public static void TestCompletionsPaths_pathMapping_parentDirectory(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/a.ts\nimport { } from \"foo//**/\";\n// @Filename: /oof/x.ts\nexport const x = 0;\n// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \"src\",\n        \"paths\": {\n            \"foo/*\": [\"../oof/*\"]\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", Kind = LSP.CompletionItemKind.File, Detail = "x.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
