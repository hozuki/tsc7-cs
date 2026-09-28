// ported from: internal/fourslash/tests/tsxCompletion13_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxCompletion13Case
{
    public static void TestTsxCompletion13(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @skipLibCheck: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface ClickableProps {\n    children?: string;\n    className?: string;\n}\ninterface ButtonProps extends ClickableProps {\n    onClick(event?: React.MouseEvent<HTMLButtonElement>): void;\n}\ninterface LinkProps extends ClickableProps {\n    goTo: string;\n}\ndeclare function MainButton(buttonProps: ButtonProps): JSX.Element;\ndeclare function MainButton(linkProps: LinkProps): JSX.Element;\ndeclare function MainButton(props: ButtonProps | LinkProps): JSX.Element;\nlet opt = <MainButton /*1*/ />;\nlet opt = <MainButton children=\"chidlren\" /*2*/ />;\nlet opt = <MainButton onClick={()=>{}} /*3*/ />;\nlet opt = <MainButton onClick={()=>{}} ignore-prop /*4*/ />;\nlet opt = <MainButton goTo=\"goTo\" /*5*/ />;\nlet opt = <MainButton wrong /*6*/ />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "6"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["goTo", "onClick", new LSP.CompletionItem { Label = "children?", InsertText = "children", FilterText = "children", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "className?", InsertText = "className", FilterText = "className", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["goTo", "onClick", new LSP.CompletionItem { Label = "className?", InsertText = "className", FilterText = "className", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3", "4", "5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "children?", InsertText = "children", FilterText = "children", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "className?", InsertText = "className", FilterText = "className", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextOptionalMember }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
