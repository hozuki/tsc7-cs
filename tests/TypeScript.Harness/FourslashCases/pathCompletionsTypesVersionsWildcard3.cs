// ported from: internal/fourslash/tests/pathCompletionsTypesVersionsWildcard3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsTypesVersionsWildcard3Case
{
    public static void TestPathCompletionsTypesVersionsWildcard3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @resolveJsonModule: false\n// @Filename: /node_modules/foo/package.json\n{\n  \"types\": \"index.d.ts\",\n  \"typesVersions\": {\n    \">=4.3.5\": {\n      \"browser/*\": [\"dist/*\"]\n    }\n  }\n}\n// @Filename: /node_modules/foo/nope.d.ts\nexport const nope = 0;\n// @Filename: /node_modules/foo/dist/index.d.ts\nexport const index = 0;\n// @Filename: /node_modules/foo/dist/blah.d.ts\nexport const blah = 0;\n// @Filename: /node_modules/foo/dist/subfolder/one.d.ts\nexport const one = 0;\n// @Filename: /a.ts\nimport { } from \"foo//**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["browser", "nope", "dist"] } });
                f.Insert(t, "browser/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["blah", "index", "subfolder"] } });
                f.Insert(t, "subfolder/");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["one"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
