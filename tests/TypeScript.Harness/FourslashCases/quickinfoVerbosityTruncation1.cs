// ported from: internal/fourslash/tests/quickinfoVerbosityTruncation1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityTruncation1Case
{
    public static void TestQuickinfoVerbosityTruncation1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Str = string | {};\ntype FooType = Str | number;\ntype Sym = symbol | (() => void);\ntype BarType = Sym | boolean;\ninterface LotsOfProps {\n    someLongPropertyName1: Str;\n    someLongPropertyName2: FooType;\n    someLongPropertyName3: Sym;\n    someLongPropertyName4: BarType;\n    someLongPropertyName5: Str;\n    someLongPropertyName6: FooType;\n    someLongPropertyName7: Sym;\n    someLongPropertyName8: BarType;\n    someLongMethodName1(a: FooType, b: BarType): Sym;\n    someLongPropertyName9: Str;\n    someLongPropertyName10: FooType;\n    someLongPropertyName11: Sym;\n    someLongPropertyName12: BarType;\n    someLongPropertyName13: Str;\n    someLongPropertyName14: FooType;\n    someLongPropertyName15: Sym;\n    someLongPropertyName16: BarType;\n    someLongMethodName2(a: FooType, b: BarType): Sym;\n}\nconst obj1/*o1*/: LotsOfProps = undefined as any as LotsOfProps;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["o1"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
