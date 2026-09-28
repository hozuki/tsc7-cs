// ported from: internal/fourslash/tests/quickinfoVerbosityObjectType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityObjectType1Case
{
    public static void TestQuickinfoVerbosityObjectType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Str = string | {};\ntype FooType = Str | number;\ntype Sym = symbol | (() => void);\ntype BarType = Sym | boolean;\ntype Obj = { foo: FooType, bar: BarType, str: Str };\nconst obj1/*o1*/: Obj = { foo: 1, bar: true, str: \"3\"};\nconst obj2/*o2*/: { foo: FooType, bar: BarType, str: Str } = { foo: 1, bar: true, str: \"3\"};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["o1"] = [0, 1, 2, 3], ["o2"] = [0, 1, 2] });
            }
            finally
            {
                done();
            }
        });
    }

}
