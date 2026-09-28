// ported from: internal/fourslash/tests/renameInheritedProperties8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameInheritedProperties8Case
{
    public static void TestRenameInheritedProperties8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C implements D {\n    [|[|{| \"contextRangeIndex\": 0 |}prop1|]: string;|]\n}\n\ninterface D extends C {\n    [|[|{| \"contextRangeIndex\": 2 |}prop1|]: string;|]\n}\n\nvar c: C;\nc.[|prop1|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "prop1");
            }
            finally
            {
                done();
            }
        });
    }

}
