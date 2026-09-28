// ported from: internal/fourslash/tests/pathCompletionsPackageJsonExportsWildcard9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonExportsWildcard9Case
{
    public static void TestPathCompletionsPackageJsonExportsWildcard9(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @allowJs: true\n// @maxNodeModuleJsDepth: 1\n// @Filename: /node_modules/foo/package.json\n{\n  \"name\": \"foo\",\n  \"exports\": {\n    \"./*\": \"./dist/*.js\"\n  }\n}\n// @Filename: /node_modules/foo/dist/blah.js\nexport const blah = 0;\n// @Filename: /index.mts\nimport { } from \"foo//**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "blah", Kind = LSP.CompletionItemKind.File, Detail = "blah.js" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
