// ported from: internal/fourslash/tests/completionInfoWithExplicitTypeArguments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionInfoWithExplicitTypeArgumentsCase
{
    public static void TestCompletionInfoWithExplicitTypeArguments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I { x: number; y: number; }\n\ndeclare function f<T>(x: T, y: number): void;\nf<I>({ /*f*/ });\n\ndeclare function g<T>(x: keyof T, y: number): void;\ng<I>(\"[|/*g*/|]\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "f", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x" }, new LSP.CompletionItem { Label = "y" }] } });
                f.VerifyCompletions(t, "g", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "x", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "y", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "y", Range = f.Ranges()[0].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
