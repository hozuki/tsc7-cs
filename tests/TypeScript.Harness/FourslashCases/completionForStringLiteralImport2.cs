// ported from: internal/fourslash/tests/completionForStringLiteralImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralImport2Case
{
    public static void TestCompletionForStringLiteralImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @typeRoots: my_typings\n// @Filename: test.ts\n/// <reference path=\"./[|some|]/*0*/\n/// <reference types=\"[|some|]/*1*/\n/// <reference path=\"./sub/[|some|]/*2*/\" />\n/// <reference types=\"[|some|]/*3*/\" />\n// @Filename: someFile.ts\n/*someFile*/\n// @Filename: sub/someOtherFile.ts\n/*someOtherFile*/\n// @Filename: my_typings/some-module/index.d.ts\nexport var x = 9;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["someFile.ts", "my_typings", "sub"] } });
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["some-module"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["someOtherFile.ts"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["some-module"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
