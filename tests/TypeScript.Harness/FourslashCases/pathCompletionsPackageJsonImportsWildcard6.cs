// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsWildcard6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsWildcard6Case
{
    public static void TestPathCompletionsPackageJsonImportsWildcard6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /package.json\n{\n  \"name\": \"foo\",\n  \"main\": \"dist/index.js\",\n  \"module\": \"dist/index.mjs\",\n  \"types\": \"dist/index.d.ts\",\n  \"imports\": {\n    \"#*\": \"./dist/*?.d.ts\"\n  }\n}\n// @Filename: /dist/index.d.ts\nexport const index = 0;\n// @Filename: /dist/blah?.d.ts\nexport const blah = 0;\n// @Filename: /index.mts\nimport { } from \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "#blah", Kind = LSP.CompletionItemKind.File, Detail = "#blah.d.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
