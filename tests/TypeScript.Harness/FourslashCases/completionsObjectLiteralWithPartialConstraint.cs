// ported from: internal/fourslash/tests/completionsObjectLiteralWithPartialConstraint_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsObjectLiteralWithPartialConstraintCase
{
    public static void TestCompletionsObjectLiteralWithPartialConstraint(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface MyOptions {\n    hello?: boolean;\n    world?: boolean;\n}\ndeclare function bar<T extends MyOptions>(options?: Partial<T>): void;\nbar({ hello: true, /*1*/ });\n\ninterface Test {\n    keyPath?: string;\n    autoIncrement?: boolean;\n}\n\nfunction test<T extends Record<string, Test>>(opt: T) { }\n\ntest({\n    a: {\n        keyPath: 'x.y',\n        autoIncrement: true\n    },\n    b: {\n        /*2*/\n    }\n});\ntype Colors = {\n    rgb: { r: number, g: number, b: number };\n    hsl: { h: number, s: number, l: number }\n};\n\nfunction createColor<T extends keyof Colors>(kind: T, values: Colors[T]) { }\n\ncreateColor('rgb', {\n  /*3*/\n});\n\ndeclare function f<T extends 'a' | 'b', U extends { a?: string }, V extends { b?: string }>(x: T, y: { a: U, b: V }[T]): void;\n\nf('a', {\n  /*4*/\n});\n\ndeclare function f2<T extends { x?: string }>(x: T): void;\nf2({\n  /*5*/\n});\n\ntype X = { a: { a }, b: { b } }\n\nfunction f4<T extends 'a' | 'b'>(p: { kind: T } & X[T]) { }\n\nf4({\n    kind: \"a\",\n    /*6*/\n})"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "world?", InsertText = "world", FilterText = "world", SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "autoIncrement?", InsertText = "autoIncrement", FilterText = "autoIncrement", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "keyPath?", InsertText = "keyPath", FilterText = "keyPath", SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["b", "g", "r"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a?", InsertText = "a", FilterText = "a", SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x?", InsertText = "x", FilterText = "x", SortText = LS.SortTextOptionalMember }] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
