// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsCustomConditions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsCustomConditionsCase
{
    public static void TestPathCompletionsPackageJsonImportsCustomConditions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @customConditions: custom-condition\n// @Filename: /package.json\n{\n  \"name\": \"foo\",\n  \"imports\": {\n    \"#only-with-custom-conditions\": {\n      \"custom-condition\": \"./something.js\"\n    }\n  }\n}\n// @Filename: /something.d.ts\nexport const index = 0;\n// @Filename: /index.ts\nimport { } from \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "#only-with-custom-conditions", Kind = LSP.CompletionItemKind.File, Detail = "#only-with-custom-conditions.js" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
