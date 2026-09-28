// ported from: internal/fourslash/tests/completionForStringLiteral4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteral4Case
{
    public static void TestCompletionForStringLiteral4(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: in.js\n/** I am documentation\n * @param {'literal'} p1\n * @param {\"literal\"} p2\n * @param {'other1' | 'other2'} p3\n * @param {'literal' | number} p4\n * @param {12 | true} p5\n */\nfunction f(p1, p2, p3, p4, p5) {\n    return p1 + p2 + p3 + p4 + p5 + '.';\n}\nf/*1*/('literal', 'literal', \"[|o/*2*/ther1|]\", 12);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoExists(t);
                f.VerifyQuickInfoIs(t, "function f(p1: \"literal\", p2: \"literal\", p3: \"other1\" | \"other2\", p4: \"literal\" | number, p5: 12 | true): string", "I am documentation");
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "other1", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "other1", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "other2", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "other2", Range = f.Ranges()[0].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
