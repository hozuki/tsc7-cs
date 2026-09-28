// ported from: internal/fourslash/tests/completionsWithOptionalPropertiesGenericPartial_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsWithOptionalPropertiesGenericPartialCase
{
    public static void TestCompletionsWithOptionalPropertiesGenericPartial(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ninterface Foo {\n    a_a: boolean;\n    a_b: boolean;\n    a_c: boolean;\n    b_a: boolean;\n}\nfunction partialFoo<T extends Partial<Foo>>(t: T) {return t}\npartialFoo({ /*1*/ });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a_a?", InsertText = "a_a", FilterText = "a_a", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "a_b?", InsertText = "a_b", FilterText = "a_b", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "a_c?", InsertText = "a_c", FilterText = "a_c", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "b_a?", InsertText = "b_a", FilterText = "b_a", SortText = LS.SortTextOptionalMember }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
