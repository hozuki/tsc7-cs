// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMemberTypeAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMemberTypeAliasCase
{
    public static void TestCodeFixClassImplementInterfaceMemberTypeAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type MyType = [string, number];\ninterface I { x: MyType; test(a: MyType): void; }\nclass C implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "type MyType = [string, number];\ninterface I { x: MyType; test(a: MyType): void; }\nclass C implements I {\n    x: MyType;\n    test(a: MyType): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
