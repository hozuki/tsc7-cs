// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceOptionalProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceOptionalPropertyCase
{
    public static void TestCodeFixClassImplementInterfaceOptionalProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface IPerson {\n    name: string;\n    birthday?: string;\n}\nclass Person implements IPerson {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'IPerson'", NewFileContent = "interface IPerson {\n    name: string;\n    birthday?: string;\n}\nclass Person implements IPerson {\n    name: string;\n    birthday?: string;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
