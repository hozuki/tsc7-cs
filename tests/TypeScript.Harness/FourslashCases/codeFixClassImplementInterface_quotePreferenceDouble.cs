// ported from: internal/fourslash/tests/codeFixClassImplementInterface_quotePreferenceDouble_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterface_quotePreferenceDoubleCase
{
    public static void TestCodeFixClassImplementInterface_quotePreferenceDouble(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    a(): void;\n    b(x: \"x\", y: \"a\" | \"b\"): \"b\";\n\n    c: \"c\";\n    d: { e: \"e\"; };\n}\nclass Foo implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "interface I {\n    a(): void;\n    b(x: \"x\", y: \"a\" | \"b\"): \"b\";\n\n    c: \"c\";\n    d: { e: \"e\"; };\n}\nclass Foo implements I {\n    a(): void {\n        throw new Error(\"Method not implemented.\");\n    }\n    b(x: \"x\", y: \"a\" | \"b\"): \"b\" {\n        throw new Error(\"Method not implemented.\");\n    }\n    c: \"c\";\n    d: { e: \"e\"; };\n}", Index = 0, UserPreferences = new LSUP.UserPreferences { QuotePreference = new LSUP.QuotePreference("double") } });
            }
            finally
            {
                done();
            }
        });
    }

}
