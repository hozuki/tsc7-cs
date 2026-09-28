// ported from: internal/fourslash/tests/completionListInNamedFunctionExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInNamedFunctionExpressionCase
{
    public static void TestCompletionListInNamedFunctionExpression(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(a: number): string {\n    /*insideFunctionDeclaration*/\n    return \"\";\n}\n\n(function foo(): number {\n    /*insideFunctionExpression*/\n    fo/*referenceInsideFunctionExpression*/o;\n    return \"\";\n})\n\n/*globalScope*/\nfo/*referenceInGlobalScope*/o;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["globalScope", "insideFunctionDeclaration", "insideFunctionExpression"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["foo"] } });
                f.VerifyQuickInfoAt(t, "referenceInsideFunctionExpression", "(local function) foo(): number", "");
                f.VerifyQuickInfoAt(t, "referenceInGlobalScope", "function foo(a: number): string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
