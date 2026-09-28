// ported from: internal/fourslash/tests/renameRestBindingElement_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameRestBindingElementCase
{
    public static void TestRenameRestBindingElement(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    a: number;\n    b: number;\n    c: number;\n}\nfunction foo([|{ a, ...[|{| \"contextRangeIndex\": 0 |}rest|] }: I|]) {\n    [|rest|];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue }, f.Ranges()[1]);
            }
            finally
            {
                done();
            }
        });
    }

}
