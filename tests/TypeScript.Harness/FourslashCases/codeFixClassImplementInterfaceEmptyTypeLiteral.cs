// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceEmptyTypeLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceEmptyTypeLiteralCase
{
    public static void TestCodeFixClassImplementInterfaceEmptyTypeLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface I {\n    x: {};\n}\n\nclass C implements I {[|\n   |]constructor() { }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "\ninterface I {\n    x: {};\n}\n\nclass C implements I {\n   constructor() { }\n    x: {};\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
