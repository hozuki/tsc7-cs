// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceObjectLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceObjectLiteralCase
{
    public static void TestCodeFixClassImplementInterfaceObjectLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IPerson {\n    coordinate: {\n        x: number;\n        y: number;\n    }\n}\nclass Person implements IPerson { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'IPerson'", NewFileContent = "interface IPerson {\n    coordinate: {\n        x: number;\n        y: number;\n    }\n}\nclass Person implements IPerson {\n    coordinate: { x: number; y: number; };\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
