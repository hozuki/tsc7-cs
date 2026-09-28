// ported from: internal/fourslash/tests/quickinfoVerbosityTypeParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityTypeParameterCase
{
    public static void TestQuickinfoVerbosityTypeParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Str = string | {};\ntype FooType = Str | number;\nfunction fn<T extends FooType>(x: T) {\n    x/*x*/;\n}\nconst y/*y*/: <T extends FooType>(x: T) => void = fn;\ntype MixinCtor<A> = new () => A/*a*/ & { constructor: MixinCtor<A> };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["x"] = [0, 1, 2], ["y"] = [0, 1, 2], ["a"] = [0] });
            }
            finally
            {
                done();
            }
        });
    }

}
