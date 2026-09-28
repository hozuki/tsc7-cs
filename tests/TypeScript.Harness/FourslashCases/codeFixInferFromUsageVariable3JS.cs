// ported from: internal/fourslash/tests/codeFixInferFromUsageVariable3JS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixInferFromUsageVariable3JSCase
{
    public static void TestCodeFixInferFromUsageVariable3JS(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @noEmit: true\n// @noImplicitAny: false\n// @Filename: important.js\n[|function f(foo) {\n    foo += 2\n    return foo\n}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "/** \n * @param {number} foo\n */\nfunction f(foo) {\n    foo += 2\n    return foo\n}\n", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
