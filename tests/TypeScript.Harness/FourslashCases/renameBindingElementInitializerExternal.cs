// ported from: internal/fourslash/tests/renameBindingElementInitializerExternal_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameBindingElementInitializerExternalCase
{
    public static void TestRenameBindingElementInitializerExternal(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n[|const [|{| \"contextRangeIndex\": 0 |}external|] = true;|]\n\nfunction f({\n    lvl1 = [|external|],\n    nested: { lvl2 = [|external|]},\n    oldName: newName = [|external|]\n}) {}\n\nconst {\n    lvl1 = [|external|],\n    nested: { lvl2 = [|external|]},\n    oldName: newName = [|external|]\n} = obj;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "external");
            }
            finally
            {
                done();
            }
        });
    }

}
