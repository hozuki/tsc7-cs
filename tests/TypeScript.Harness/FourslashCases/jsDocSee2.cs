// ported from: internal/fourslash/tests/jsDocSee2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocSee2Case
{
    public static void TestJsDocSee2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** @see {/*use1*/[|foooo|]} unknown reference*/\nconst a = \"\"\n/** @see {/*use2*/[|@bar|]} invalid tag*/\nconst b = \"\"\n/** @see /*use3*/[|foooo|] unknown reference without brace*/\nconst c = \"\"\n/** @see /*use4*/[|@bar|] invalid tag without brace*/\nconst [|/*def1*/d|] = \"\"\n/** @see {/*use5*/[|d@fff|]} partial reference */\nconst e = \"\"\n/** @see /*use6*/[|@@@@@@|] total invalid tag*/\nconst f = \"\"\n/** @see d@{/*use7*/[|fff|]} partial reference */\nconst g = \"\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "use1", "use2", "use3", "use4", "use5", "use6", "use7");
            }
            finally
            {
                done();
            }
        });
    }

}
