// ported from: internal/fourslash/tests/completionsGenericIndexedAccess4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsGenericIndexedAccess4Case
{
    public static void TestCompletionsGenericIndexedAccess4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface CustomElements {\n  'component-one': {\n      foo?: string;\n  },\n  'component-two': {\n      bar?: string;\n  }\n}\n\ninterface Options<T extends keyof CustomElements> {\n  props: CustomElements[T];\n}\n\ndeclare function create<T extends 'hello' | 'goodbye'>(name: T, options: Options<T extends 'hello' ? 'component-one' : 'component-two'>): void;\ndeclare function create<T extends keyof CustomElements>(name: T, options: Options<T>): void;\n\ncreate('hello', { props: { /*1*/ } })\ncreate('goodbye', { props: { /*2*/ } })\ncreate('component-one', { props: { /*3*/ } });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "foo?", InsertText = "foo", FilterText = "foo", SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "bar?", InsertText = "bar", FilterText = "bar", SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "foo?", InsertText = "foo", FilterText = "foo", SortText = LS.SortTextOptionalMember }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
