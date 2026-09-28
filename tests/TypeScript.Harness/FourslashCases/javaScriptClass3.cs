// ported from: internal/fourslash/tests/javaScriptClass3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JavaScriptClass3Case
{
    public static void TestJavaScriptClass3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: Foo.js\nclass Foo {\n   constructor() {\n       this./*dst1*/alpha = 10;\n       this./*dst2*/beta = 'gamma';\n   }\n   method() { return this.alpha; }\n}\nvar x = new Foo();\nx.[|alpha/*src1*/|];\nx.[|beta/*src2*/|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "src1", "src2");
            }
            finally
            {
                done();
            }
        });
    }

}
