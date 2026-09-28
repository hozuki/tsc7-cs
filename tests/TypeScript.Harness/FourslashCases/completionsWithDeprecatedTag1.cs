// ported from: internal/fourslash/tests/completionsWithDeprecatedTag1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsWithDeprecatedTag1Case
{
    public static void TestCompletionsWithDeprecatedTag1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @filename: /foobar.ts\n/** @deprecated */\nexport function foobar() {}\n// @filename: /foo.ts\nimport { foobar/*4*/ } from \"./foobar\";\n\n/** @deprecated */\ninterface Foo {\n    /** @deprecated */\n    bar(): void\n    /** @deprecated */\n    prop: number\n}\ndeclare const foo: Foo;\ndeclare const foooo: Fo/*1*/;\nfoo.ba/*2*/;\nfoo.pro/*3*/;\n\nfooba/*5*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Foo", Kind = LSP.CompletionItemKind.Interface, SortText = LS.DeprecateSortText(LS.SortTextLocationPriority), Tags = [LSP.CompletionItemTag.Deprecated] }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "bar", Kind = LSP.CompletionItemKind.Method, SortText = LS.DeprecateSortText(LS.SortTextLocationPriority), Tags = [LSP.CompletionItemTag.Deprecated] }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "prop", Kind = LSP.CompletionItemKind.Field, SortText = LS.DeprecateSortText(LS.SortTextLocationPriority), Tags = [LSP.CompletionItemTag.Deprecated] }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foobar", Kind = LSP.CompletionItemKind.Function, SortText = LS.DeprecateSortText(LS.SortTextLocationPriority), Tags = [LSP.CompletionItemTag.Deprecated] }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foobar", Kind = LSP.CompletionItemKind.Variable, SortText = LS.DeprecateSortText(LS.SortTextLocationPriority), Tags = [LSP.CompletionItemTag.Deprecated] }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
