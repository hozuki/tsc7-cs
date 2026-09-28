// ported from: internal/fourslash/tests/signatureHelpCallExpressionJs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpCallExpressionJsCase
{
    public static void TestSignatureHelpCallExpressionJs(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @checkJs: true\n// @allowJs: true\n// @Filename: main.js\nfunction allOptional() { arguments; }\nallOptional(/*1*/);\nallOptional(1, 2, 3);\nfunction someOptional(x, y) { arguments; }\nsomeOptional(/*2*/);\nsomeOptional(1, 2, 3);\nsomeOptional(); // no error here; x and y are optional in JS"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "allOptional(...args: any[]): void", ParameterCount = 1, ParameterName = "args", ParameterSpan = "...args: any[]", IsVariadic = true, IsVariadicSet = true });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "someOptional(x: any, y: any, ...args: any[]): void", ParameterCount = 3, ParameterName = "x", ParameterSpan = "x: any", IsVariadic = true, IsVariadicSet = true });
            }
            finally
            {
                done();
            }
        });
    }

}
