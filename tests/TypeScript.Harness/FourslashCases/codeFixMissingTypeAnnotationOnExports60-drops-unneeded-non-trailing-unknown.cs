// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports60-drops-unneeded-non-trailing-unknown_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports60_drops_unneeded_non_trailing_unknownCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports60_drops_unneeded_non_trailing_unknown(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n\nexport interface Foo<S = string, T = unknown> {}\nexport function f(x: Foo<string, unknown>) { return x; }\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'Foo'", NewFileContent = "\nexport interface Foo<S = string, T = unknown> {}\nexport function f(x: Foo<string, unknown>): Foo { return x; }\n", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
