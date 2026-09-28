// ported from: internal/fourslash/tests/navigationBarInitializerSpans_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarInitializerSpansCase
{
    public static void TestNavigationBarInitializerSpans(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// get the name for the navbar from the variable name rather than the function name\nconst [|[|x|] = () => { var [|a|]; }|];\nconst [|[|f|] = function f() { var [|b|]; }|];\nconst [|[|y|] = { [|[|z|]: function z() { var [|c|]; }|] }|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
