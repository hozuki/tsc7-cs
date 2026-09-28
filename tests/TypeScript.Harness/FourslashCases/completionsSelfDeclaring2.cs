// ported from: internal/fourslash/tests/completionsSelfDeclaring2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsSelfDeclaring2Case
{
    public static void TestCompletionsSelfDeclaring2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nfunction f1<T>(x: T) {}\nf1({ [|abc|]/*1*/ });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = new F.EditRange { Insert = f.Ranges()[0], Replace = f.Ranges()[0] } }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus(["f1"], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
