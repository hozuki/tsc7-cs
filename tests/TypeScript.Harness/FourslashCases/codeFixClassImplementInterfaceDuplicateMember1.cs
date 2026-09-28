// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceDuplicateMember1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceDuplicateMember1Case
{
    public static void TestCodeFixClassImplementInterfaceDuplicateMember1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I1 {\n    x: number;\n}\ninterface I2 {\n    x: number;\n}\n\nclass C implements I1,I2 {[| |]}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Implement interface 'I1'", "Implement interface 'I2'"]);
            }
            finally
            {
                done();
            }
        });
    }

}
