// ported from: internal/fourslash/tests/renameAlias2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameAlias2Case
{
    public static void TestRenameAlias2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|module [|{| \"contextRangeIndex\": 0 |}SomeModule|] { export class SomeClass { } }|]\nimport M = [|SomeModule|];\nimport C = M.SomeClass;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "SomeModule");
            }
            finally
            {
                done();
            }
        });
    }

}
