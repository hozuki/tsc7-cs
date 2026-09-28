// ported from: internal/fourslash/tests/jsxAriaLikeCompletions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsxAriaLikeCompletionsCase
{
    public static void TestJsxAriaLikeCompletions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\ndeclare var React: any;\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n        div: { \"aria-whatever\"?: string  }\n    }\n    interface ElementAttributesProperty { props: any }\n}\nconst a = <div {...{}} /*1*/></div>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "aria-whatever?", InsertText = "aria-whatever", FilterText = "aria-whatever", SortText = LS.SortTextOptionalMember }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
