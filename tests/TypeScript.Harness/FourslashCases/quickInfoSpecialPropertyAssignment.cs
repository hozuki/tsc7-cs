// ported from: internal/fourslash/tests/quickInfoSpecialPropertyAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoSpecialPropertyAssignmentCase
{
    public static void TestQuickInfoSpecialPropertyAssignment(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\nclass C {\n    constructor() {\n      /** Doc */\n      this./*write*/x = 0;\n      this./*read*/x;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "write", "(property) C.x: any", "Doc");
                f.VerifyQuickInfoAt(t, "read", "(property) C.x: number", "Doc");
            }
            finally
            {
                done();
            }
        });
    }

}
