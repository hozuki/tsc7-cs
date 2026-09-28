// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceNoBody_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceNoBodyCase
{
    public static void TestCodeFixClassImplementInterfaceNoBody(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n   m(): void\n}\nclass C/*c*/ implements I"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyErrorExistsBeforeMarker(t, "c");
                f.GoToMarker(t, "c");
                f.VerifyCodeFixAvailable(t, ["Implement interface 'I'"]);
            }
            finally
            {
                done();
            }
        });
    }

}
