// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceTypeParamInstantiateU_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceTypeParamInstantiateUCase
{
    public static void TestCodeFixClassImplementInterfaceTypeParamInstantiateU(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I<T> { x: T; }\nclass C<U> implements I<U> {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I<U>'", NewFileContent = "interface I<T> { x: T; }\nclass C<U> implements I<U> {\n    x: U;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
