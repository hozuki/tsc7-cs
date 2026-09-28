// ported from: internal/fourslash/tests/jsdocTypedefTagRename02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagRename02Case
{
    public static void TestJsdocTypedefTagRename02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @allowNonTsExtensions: true\n// @Filename: jsDocTypedef_form2.js\n\n/** [|@typedef {(string | number)} [|{| \"contextRangeIndex\": 0 |}NumberLike|]|] */\n\n/** @type {[|NumberLike|]} */\nvar numberLike;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineRename(t, null!, U.ToAny(f.Ranges().Skip(1)));
            }
            finally
            {
                done();
            }
        });
    }

}
