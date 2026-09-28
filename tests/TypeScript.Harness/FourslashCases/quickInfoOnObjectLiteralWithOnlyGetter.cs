// ported from: internal/fourslash/tests/quickInfoOnObjectLiteralWithOnlyGetter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnObjectLiteralWithOnlyGetterCase
{
    public static void TestQuickInfoOnObjectLiteralWithOnlyGetter(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function /*1*/makePoint(x: number) {\n    return {\n        get x() { return x; },\n    };\n};\nvar /*4*/point = makePoint(2);\nvar /*2*/x = point./*3*/x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "function makePoint(x: number): {\n    readonly x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "2", "var x: number", "");
                f.VerifyQuickInfoAt(t, "4", "var point: {\n    readonly x: number;\n}", "");
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", Detail = "(property) x: number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
