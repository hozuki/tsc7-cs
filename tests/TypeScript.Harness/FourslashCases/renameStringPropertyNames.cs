// ported from: internal/fourslash/tests/renameStringPropertyNames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameStringPropertyNamesCase
{
    public static void TestRenameStringPropertyNames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var o = {\n    [|[|{| \"contextRangeIndex\": 0 |}prop|]: 0|]\n};\n\no = {\n    [|\"[|{| \"contextRangeIndex\": 2 |}prop|]\": 1|]\n};\n\no[\"[|prop|]\"];\no['[|prop|]'];\no.[|prop|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "prop");
            }
            finally
            {
                done();
            }
        });
    }

}
