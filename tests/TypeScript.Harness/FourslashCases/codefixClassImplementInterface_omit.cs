// ported from: internal/fourslash/tests/codefixClassImplementInterface_omit_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodefixClassImplementInterface_omitCase
{
    public static void TestCodefixClassImplementInterface_omit(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface One {\n    a: number;\n    b: string;\n}\n\ninterface Two extends Omit<One, \"a\"> {\n    c: boolean;\n}\n\nclass TwoStore implements Two {[| |]}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Two'", NewFileContent = "interface One {\n    a: number;\n    b: string;\n}\n\ninterface Two extends Omit<One, \"a\"> {\n    c: boolean;\n}\n\nclass TwoStore implements Two {\n    c: boolean;\n    b: string;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
