// ported from: internal/fourslash/tests/renameNameOnEnumMember_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameNameOnEnumMemberCase
{
    public static void TestRenameNameOnEnumMember(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("enum e {\n    firstMember,\n    secondMember,\n    thirdMember\n}\nvar enumMember = e.[|/**/thirdMember|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyRenameSucceeded(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
