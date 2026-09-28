// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMultipleMembersAndPunctuation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMultipleMembersAndPunctuationCase
{
    public static void TestCodeFixClassImplementInterfaceMultipleMembersAndPunctuation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I1 {\n    x: number,\n    y: number\n    z: number;\n    f(): number,\n    g(): any\n    h();\n}\n\nclass C1 implements I1 {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I1'", NewFileContent = "interface I1 {\n    x: number,\n    y: number\n    z: number;\n    f(): number,\n    g(): any\n    h();\n}\n\nclass C1 implements I1 {\n    x: number;\n    y: number;\n    z: number;\n    f(): number {\n        throw new Error(\"Method not implemented.\");\n    }\n    g() {\n        throw new Error(\"Method not implemented.\");\n    }\n    h() {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
