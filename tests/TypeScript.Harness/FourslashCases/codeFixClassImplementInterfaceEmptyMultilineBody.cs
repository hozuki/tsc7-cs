// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceEmptyMultilineBody_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceEmptyMultilineBodyCase
{
    public static void TestCodeFixClassImplementInterfaceEmptyMultilineBody(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es2017\ninterface I {\n    x: number;\n    y: number;\n}\nclass C implements I {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "interface I {\n    x: number;\n    y: number;\n}\nclass C implements I {\n    x: number;\n    y: number;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
