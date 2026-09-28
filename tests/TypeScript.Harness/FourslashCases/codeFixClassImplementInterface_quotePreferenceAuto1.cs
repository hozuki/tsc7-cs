// ported from: internal/fourslash/tests/codeFixClassImplementInterface_quotePreferenceAuto1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterface_quotePreferenceAuto1Case
{
    public static void TestCodeFixClassImplementInterface_quotePreferenceAuto1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: a.ts\nexport interface I {\n    a(): void;\n    b(x: \"x\", y: \"a\" | \"b\"): \"b\";\n\n    c: \"c\";\n    d: { e: \"e\"; };\n}\n// @filename: b.ts\nimport { I } from \"./a\";\nclass Foo implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "b.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "import { I } from \"./a\";\nclass Foo implements I {\n    a(): void {\n        throw new Error(\"Method not implemented.\");\n    }\n    b(x: \"x\", y: \"a\" | \"b\"): \"b\" {\n        throw new Error(\"Method not implemented.\");\n    }\n    c: \"c\";\n    d: { e: \"e\"; };\n}", Index = 0, UserPreferences = new LSUP.UserPreferences { QuotePreference = new LSUP.QuotePreference("auto") } });
            }
            finally
            {
                done();
            }
        });
    }

}
