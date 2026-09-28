// ported from: internal/fourslash/tests/completionInFunctionLikeBody_includesPrimitiveTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionInFunctionLikeBody_includesPrimitiveTypesCase
{
    public static void TestCompletionInFunctionLikeBody_includesPrimitiveTypes(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo<T> { }\nclass Bar { }\nfunction includesTypes() {\n    new Foo</*1*/\n}\nfunction excludesTypes1() {\n    new Bar</*2*/\n}\nfunction excludesTypes2() {\n    1</*3*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "string", SortText = LS.SortTextGlobalsOrKeywords }, new LSP.CompletionItem { Label = "String", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["string"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
