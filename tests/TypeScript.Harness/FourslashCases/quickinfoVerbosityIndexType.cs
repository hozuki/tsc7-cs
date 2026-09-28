// ported from: internal/fourslash/tests/quickinfoVerbosityIndexType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityIndexTypeCase
{
    public static void TestQuickinfoVerbosityIndexType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface T1 {\n\tbanana: string;\n\tgrape: number;\n\tapple: boolean;\n}\nconst x1/*x1*/: keyof T1 = 'banana';\nconst x2/*x2*/: keyof T1 & (\"grape\" | \"apple\") = 'grape';\nfunction fn1<T extends T1>(obj: T, key: keyof T, k2: keyof T1) {\n\tif (key === k2/*k2*/) {\n\t\treturn obj[key/*key*/];\n\t}\n\treturn key;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["x1"] = [0, 1], ["x2"] = [0], ["k2"] = [0, 1], ["key"] = [0] });
            }
            finally
            {
                done();
            }
        });
    }

}
