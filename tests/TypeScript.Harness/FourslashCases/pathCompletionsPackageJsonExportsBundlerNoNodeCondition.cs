// ported from: internal/fourslash/tests/pathCompletionsPackageJsonExportsBundlerNoNodeCondition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonExportsBundlerNoNodeConditionCase
{
    public static void TestPathCompletionsPackageJsonExportsBundlerNoNodeCondition(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /node_modules/foo/package.json\n{\n  \"name\": \"foo\",\n  \"exports\": {\n    \"./only-for-node\": {\n      \"node\": \"./something.js\"\n    },\n    \"./for-everywhere\": \"./other.js\"\n  }\n}\n// @Filename: /node_modules/foo/something.d.ts\nexport const index = 0;\n// @Filename: /node_modules/foo/other.d.ts\nexport const index = 0;\n// @Filename: /index.ts\nimport { } from \"foo//**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "for-everywhere", Kind = LSP.CompletionItemKind.File, Detail = "for-everywhere.js" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
