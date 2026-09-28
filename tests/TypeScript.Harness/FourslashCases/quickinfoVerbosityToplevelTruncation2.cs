// ported from: internal/fourslash/tests/quickinfoVerbosityToplevelTruncation2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityToplevelTruncation2Case
{
    public static void TestQuickinfoVerbosityToplevelTruncation2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export enum LargeEnum/*1*/ {\n    Member1,\n    Member2,\n    Member3,\n    Member4,\n    Member5,\n    Member6,\n    Member7,\n    Member8,\n    Member9,\n    Member10,\n    Member11,\n    Member12,\n    Member13,\n    Member14,\n    Member15,\n    Member16,\n    Member17,\n    Member18,\n    Member19,\n    Member20,\n    Member21,\n    Member22,\n    Member23,\n    Member24,\n    Member25,\n}\nexport interface LargeInterface/*2*/ {\n    property1: string;\n    property2: number;\n    property3: boolean;\n    property4: Date;\n    property5: string[];\n    property6: number[];\n    property7: boolean[];\n    property8: { [key: string]: unknown };\n    property9: string | null;\n    property10: number | null;\n    property11: boolean | null;\n    property12: Date | null;\n    property13: string | number;\n    property14: number | boolean;\n    property15: string | boolean;\n    property16: Array<{ id: number; name: string }>;\n    property17: Array<{ key: string; value: unknown }>;\n    property18: { nestedProp1: string; nestedProp2: number };\n    property19: { nestedProp3: boolean; nestedProp4: Date };\n    property20: () => void;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [1], ["2"] = [1] });
            }
            finally
            {
                done();
            }
        });
    }

}
