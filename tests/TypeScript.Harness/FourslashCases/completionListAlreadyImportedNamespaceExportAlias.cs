// ported from: internal/fourslash/tests/completionListAlreadyImportedNamespaceExportAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAlreadyImportedNamespaceExportAliasCase
{
    public static void TestCompletionListAlreadyImportedNamespaceExportAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /values.ts\nexport const A = 1;\nexport const B = 2;\n\n// @Filename: /namespace.ts\nimport * as Group from \"./values.js\";\ntype Group = (typeof Group)[keyof typeof Group];\nexport { Group };\n\n// @Filename: /index.ts\nimport { Group } from \"./namespace.js\";\n\nconsole.log(Grou/**/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var result = f.VerifyCompletions(t, "", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Group" }] } });
                result.AndHasNoCodeAction(t, new F.CompletionsExpectedCodeAction { Name = "Group", Source = "./namespace.js" });
            }
            finally
            {
                done();
            }
        });
    }

}
