// ported from: internal/fourslash/tests/asConstRefsNoErrors1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AsConstRefsNoErrors1Case
{
    public static void TestAsConstRefsNoErrors1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "class Tex {\n    type = 'Text' as /**/const;\n}";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
