// ported from: internal/fourslash/tests/removeVarFromModuleWithReopenedEnums_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RemoveVarFromModuleWithReopenedEnumsCase
{
    public static void TestRemoveVarFromModuleWithReopenedEnums(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace A {\n    /**/var o;\n}\nenum A {\n}\nenum A {\n}\nnamespace A {\n    var p;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.DeleteAtCaret(t, 6);
            }
            finally
            {
                done();
            }
        });
    }

}
