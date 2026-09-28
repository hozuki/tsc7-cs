// ported from: internal/fourslash/tests/completionCloneQuestionToken_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionCloneQuestionTokenCase
{
    public static void TestCompletionCloneQuestionToken(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @Filename: /file2.ts\ntype TCallback<T = any> = (options: T) => any;\ntype InKeyOf<E> = { [K in keyof E]?: TCallback<E[K]>; };\nexport class Bar<A> {\n    baz(a: InKeyOf<A>): void { }\n}\n// @Filename: /file1.ts\nimport { Bar } from './file2';\ntype TwoKeys = Record<'a' | 'b', { thisFails?: any; }>\nclass Foo extends Bar<TwoKeys> {\n    /**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "baz", InsertText = "baz(a: { a?: (options: { thisFails?: any; }) => any; b?: (options: { thisFails?: any; }) => any; }): void {\n}", FilterText = "baz" }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
