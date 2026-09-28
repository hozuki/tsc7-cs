// ported from: internal/fourslash/tests/jsDocSee4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocSee4Case
{
    public static void TestJsDocSee4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class [|/*def1*/A|] {\n    foo () { }\n}\ndeclare const [|/*def2*/a|]: A;\n/**\n * @see {/*use1*/[|A|]#foo}\n */\nconst t1 = 1\n/**\n * @see {/*use2*/[|a|].foo()}\n */\nconst t2 = 1\n/**\n * @see {@link /*use3*/[|a|].foo()}\n */\nconst t3 = 1"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, false, "use1", "use2", "use3");
            }
            finally
            {
                done();
            }
        });
    }

}
