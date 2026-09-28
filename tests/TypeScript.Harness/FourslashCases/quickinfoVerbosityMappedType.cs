// ported from: internal/fourslash/tests/quickinfoVerbosityMappedType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityMappedTypeCase
{
    public static void TestQuickinfoVerbosityMappedType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Apple = boolean | number;\ntype Orange = string | boolean;\ntype F<T> = {\n\t[K in keyof T as T[K] extends Apple ? never : K]: T[K];\n}\ntype Bar = {\n\tbanana: string;\n\tapple: boolean;\n}\nconst x/*x*/: F/*F*/<Bar> = { banana: 'hello' };\nconst y/*y*/: { [K in keyof Bar]?: Bar[K] } = { banana: 'hello' };\ntype G<T> = {\n\t[K in keyof T]: T[K] & Apple\n};\nconst z: G/*G*/<Bar> = { banana: 'hello', apple: true };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["x"] = [0, 1], ["y"] = [0], ["F"] = [0, 1], ["G"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
