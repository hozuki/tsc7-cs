// ported from: internal/fourslash/tests/completionForStringLiteralWithDynamicImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralWithDynamicImportCase
{
    public static void TestCompletionForStringLiteralWithDynamicImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @typeRoots: fourslash/my_typings\n// @Filename: fourslash/test.ts\nconst a = import(\"./some/*0*/\nconst a = import(\"./sub/some/*1*/\");\nconst a = import(\"[|some-/*2*/|]\");\nconst a = import(\"..//*3*/\");\n// @Filename: fourslash/someFile1.ts\n/*someFile1*/\n// @Filename: fourslash/sub/someFile2.ts\n/*someFile2*/\n// @Filename: fourslash/my_typings/some-module/index.d.ts\nexport var x = 9;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["someFile1", "my_typings", "sub"] } });
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["someFile2"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "some-module", Detail = "some-module", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "some-module", Range = f.Ranges()[0].LSRange } } }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["fourslash"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
