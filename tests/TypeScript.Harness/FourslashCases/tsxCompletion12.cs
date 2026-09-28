// ported from: internal/fourslash/tests/tsxCompletion12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxCompletion12Case
{
    public static void TestTsxCompletion12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare module JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface OptionPropBag {\n    propx: number\n    propString: \"hell\"\n    optional?: boolean\n}\ndeclare function Opt(attributes: OptionPropBag): JSX.Element;\nlet opt = <Opt /*1*/ />;\nlet opt1 = <Opt [|prop|]/*2*/ />;\nlet opt2 = <Opt propx={100} /*3*/ />;\nlet opt3 = <Opt propx={100} optional /*4*/ />;\nlet opt4 = <Opt wrong /*5*/ />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["propString", "propx", new LSP.CompletionItem { Label = "optional?", InsertText = "optional", FilterText = "optional", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["propString", "propx", new LSP.CompletionItem { Label = "optional?", FilterText = "optional", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember, TextEdit = new LSP.TextEditOrInsertReplaceEdit { InsertReplaceEdit = new LSP.InsertReplaceEdit { NewText = "optional", Insert = f.Ranges()[0].LSRange, Replace = f.Ranges()[0].LSRange } } }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["propString", new LSP.CompletionItem { Label = "optional?", InsertText = "optional", FilterText = "optional", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["propString"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
