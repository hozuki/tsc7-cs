// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMappedType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMappedType1Case
{
    public static void TestCodeFixClassImplementInterfaceMappedType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I<X> {\n    x: { readonly [K in keyof X]: X[K] };\n}\nclass C<Y> implements I<Y> {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I<Y>'", NewFileContent = "interface I<X> {\n    x: { readonly [K in keyof X]: X[K] };\n}\nclass C<Y> implements I<Y> {\n    x: { readonly [K in keyof Y]: Y[K]; };\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
