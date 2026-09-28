// ported from: internal/fourslash/tests/completionsPaths_importType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsPaths_importTypeCase
{
    public static void TestCompletionsPaths_importType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @moduleResolution: bundler\n// @Filename: /ns.ts\nfile content not read\n// @Filename: /node_modules/package/index.ts\nfile content not read\n// @Filename: /usage.ts\ntype A = typeof import(\"[|p|]/*1*/\");\ntype B = import(\".//*2*/\");\n// @Filename: /user.js\n/** @type {import(\"/*3*/\")} */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "package", Kind = LSP.CompletionItemKind.Folder, Detail = "package", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "package", Range = f.Ranges()[0].LSRange } } }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "package", Kind = LSP.CompletionItemKind.Folder, Detail = "package" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "node_modules", Kind = LSP.CompletionItemKind.Folder, Detail = "node_modules" }, new LSP.CompletionItem { Label = "ns", Kind = LSP.CompletionItemKind.File, Detail = "ns.ts" }, new LSP.CompletionItem { Label = "user", Kind = LSP.CompletionItemKind.File, Detail = "user.js" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
