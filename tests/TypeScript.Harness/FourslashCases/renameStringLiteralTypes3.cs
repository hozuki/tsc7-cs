// ported from: internal/fourslash/tests/renameStringLiteralTypes3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameStringLiteralTypes3Case
{
    public static void TestRenameStringLiteralTypes3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Foo = \"[|a|]\" | \"b\";\n\nclass C {\n    p: Foo = \"[|a|]\";\n    m() {\n        switch (this.p) {\n            case \"[|a|]\":\n                return 1;\n            case \"b\":\n                return 2;\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "a");
            }
            finally
            {
                done();
            }
        });
    }

}
