// ported from: internal/fourslash/tests/jsDocSee_rename1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocSee_rename1Case
{
    public static void TestJsDocSee_rename1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|interface [|{| \"contextRangeIndex\": 0 |}A|] {}|]\n/**\n * @see {[|A|]}\n */\ndeclare const a: [|A|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, U.ToAny(f.Ranges().Skip(1)));
            }
            finally
            {
                done();
            }
        });
    }

}
