// ported from: internal/fourslash/tests/quickinfoVerbosityEnum_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityEnumCase
{
    public static void TestQuickinfoVerbosityEnum(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: a.ts\nexport {};\nenum Color/*c*/ {\n    Red,\n    Green,\n    Blue,\n}\nconst x/*x*/: Color = Color.Red;\nconst enum Direction/*d*/ {\n    Up,\n    Down,\n}\nconst y/*y*/: Direction = Direction.Up;\nenum Flags/*f*/ {\n    None = 0,\n    IsDirectory = 1 << 0,\n    IsFile = 1 << 1,\n    IsSymlink = 1 << 2,\n}\n// @filename: b.ts\nexport enum Color {\n    Red = \"red\"\n}\n// @filename: c.ts\nimport { Color } from \"./b\";\nconst c: Color/*a*/ = Color.Red;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["c"] = [0, 1], ["x"] = [0, 1], ["d"] = [0, 1], ["y"] = [0, 1], ["f"] = [0, 1], ["a"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
