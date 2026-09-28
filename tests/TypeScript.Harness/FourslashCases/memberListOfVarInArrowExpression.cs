// ported from: internal/fourslash/tests/memberListOfVarInArrowExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberListOfVarInArrowExpressionCase
{
    public static void TestMemberListOfVarInArrowExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IMap<T> {\n    [key: string]: T;\n}\nvar map: IMap<{ a1: string; }[]>;\nvar categories: string[];\neach(categories, category => {\n    var changes = map[category];\n    changes[0]./*1*/a1;\n    return each(changes, change => {\n    });\n});\nfunction each<T>(items: T[], handler: (item: T) => void) { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) a1: string", "");
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a1", Detail = "(property) a1: string" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
