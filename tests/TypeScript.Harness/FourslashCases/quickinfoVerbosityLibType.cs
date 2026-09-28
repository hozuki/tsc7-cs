// ported from: internal/fourslash/tests/quickinfoVerbosityLibType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityLibTypeCase
{
    public static void TestQuickinfoVerbosityLibType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ninterface Apple {\n    color: string;\n    size: number;\n}\nfunction f(): Promise<Apple> {\n    return Promise.resolve({ color: \"red\", size: 5 });\n}\nconst g/*g*/ = f;\nconst u/*u*/: Map<string, Apple> = new Map;\ntype Foo<T> = Promise/*p*/<T>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["g"] = [0, 1], ["u"] = [0, 1], ["p"] = [0] });
            }
            finally
            {
                done();
            }
        });
    }

}
