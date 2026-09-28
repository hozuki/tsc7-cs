// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceHeritageClauseAlreadyHaveMember_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceHeritageClauseAlreadyHaveMemberCase
{
    public static void TestCodeFixClassImplementInterfaceHeritageClauseAlreadyHaveMember(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nclass Base {\n    foo: number;\n}\n\nclass D extends Base {\n    bar: number;\n}\n\ninterface I {\n    foo: number;\n    bar: number;\n    baz: number;\n}\n\nclass C extends D implements I { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "class Base {\n    foo: number;\n}\n\nclass D extends Base {\n    bar: number;\n}\n\ninterface I {\n    foo: number;\n    bar: number;\n    baz: number;\n}\n\nclass C extends D implements I {\n    baz: number;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
