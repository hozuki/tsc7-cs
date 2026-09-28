// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMemberOrdering_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMemberOrderingCase
{
    public static void TestCodeFixClassImplementInterfaceMemberOrdering(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es2017\n/** asdf */\ninterface I {\n    1;\n    2;\n    3;\n    4;\n    5;\n    6;\n    7;\n    8;\n    9;\n    10;\n    11;\n    12;\n    13;\n    14;\n    15;\n    16;\n    17;\n    18;\n    19;\n    20;\n    21;\n    22;\n    /** a nice safe prime */\n    23;\n}\nclass C implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "/** asdf */\ninterface I {\n    1;\n    2;\n    3;\n    4;\n    5;\n    6;\n    7;\n    8;\n    9;\n    10;\n    11;\n    12;\n    13;\n    14;\n    15;\n    16;\n    17;\n    18;\n    19;\n    20;\n    21;\n    22;\n    /** a nice safe prime */\n    23;\n}\nclass C implements I {\n    1: any;\n    2: any;\n    3: any;\n    4: any;\n    5: any;\n    6: any;\n    7: any;\n    8: any;\n    9: any;\n    10: any;\n    11: any;\n    12: any;\n    13: any;\n    14: any;\n    15: any;\n    16: any;\n    17: any;\n    18: any;\n    19: any;\n    20: any;\n    21: any;\n    22: any;\n    23: any;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
