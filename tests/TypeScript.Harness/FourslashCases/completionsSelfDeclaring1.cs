// ported from: internal/fourslash/tests/completionsSelfDeclaring1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsSelfDeclaring1Case
{
    public static void TestCompletionsSelfDeclaring1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Test {\n  keyPath?: string;\n  autoIncrement?: boolean;\n}\n\nfunction test<T extends Record<string, Test>>(opt: T) { }\n\ntest({\n  a: {\n    keyPath: '',\n    [|a|]/**/\n  }\n})"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "autoIncrement?", FilterText = "autoIncrement", SortText = LS.SortTextOptionalMember, TextEdit = new LSP.TextEditOrInsertReplaceEdit { InsertReplaceEdit = new LSP.InsertReplaceEdit { NewText = "autoIncrement", Insert = f.Ranges()[0].LSRange, Replace = f.Ranges()[0].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
