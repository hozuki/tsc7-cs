// ported from: internal/fourslash/tests/renameStringLiteralTypes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameStringLiteralTypes2Case
{
    public static void TestRenameStringLiteralTypes2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Foo = \"[|a|]\" | \"b\";\n\nclass C {\n    p: Foo = \"[|a|]\";\n    m() {\n        if (this.p === \"[|a|]\") {}\n        if (\"[|a|]\" === this.p) {}\n\n        if (this.p !== \"[|a|]\") {}\n        if (\"[|a|]\" !== this.p) {}\n\n        if (this.p == \"[|a|]\") {}\n        if (\"[|a|]\" == this.p) {}\n\n        if (this.p != \"[|a|]\") {}\n        if (\"[|a|]\" != this.p) {}\n    }\n}"u8);
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
