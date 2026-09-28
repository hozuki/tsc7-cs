// ported from: internal/fourslash/tests/quickinfoVerbosityIndexedAccessType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityIndexedAccessTypeCase
{
    public static void TestQuickinfoVerbosityIndexedAccessType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface T2 {\n\t\"string key\": string;\n\t\"number key\": number;\n\t\"any key\": string | number | symbol;\n}\ntype K2 = \"string key\" | \"any key\";\nfunction fn2<T extends T2>(obj: T, key: keyof T) {\n\tconst value/*v1*/: T[K2] = undefined as any;\n}\nfunction fn3<K extends keyof T2>(obj: T2, key: K) {\n    const value/*v2*/: T2[K] = undefined as any;;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["v1"] = [0, 1], ["v2"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
