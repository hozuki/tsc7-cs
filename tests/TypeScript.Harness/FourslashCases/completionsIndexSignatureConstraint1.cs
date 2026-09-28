// ported from: internal/fourslash/tests/completionsIndexSignatureConstraint1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsIndexSignatureConstraint1Case
{
    public static void TestCompletionsIndexSignatureConstraint1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n\nrepro #9900\n\ninterface Test {\n  a?: number;\n  b?: string;\n}\n\ninterface TestIndex {\n  [key: string]: Test;\n}\n\ndeclare function testFunc<T extends TestIndex>(t: T): void;\n\ntestFunc({\n  test: {\n    /**/\n  },\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a?", InsertText = "a", FilterText = "a", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "b?", InsertText = "b", FilterText = "b", SortText = LS.SortTextOptionalMember }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
