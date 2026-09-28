// ported from: internal/fourslash/tests/pathCompletionsPackageJsonExportsWildcard11_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonExportsWildcard11Case
{
    public static void TestPathCompletionsPackageJsonExportsWildcard11(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: preserve\n// @moduleResolution: bundler\n// @jsx: react\n// @Filename: /node_modules/repo/package.json\n{\n  \"name\": \"repo\",\n  \"exports\": {\n    \"./*\": \"./src/*\"\n  }\n}\n// @Filename: /node_modules/repo/src/card.tsx\nexport {};\n// @Filename: /main.ts\nimport { } from \"repo//**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "card.js", Kind = LSP.CompletionItemKind.File, Detail = "card.js" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
