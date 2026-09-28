// ported from: internal/fourslash/tests/jsxWithTypeParametershasInstantiatedSignatureHelp_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsxWithTypeParametershasInstantiatedSignatureHelpCase
{
    public static void TestJsxWithTypeParametershasInstantiatedSignatureHelp(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare namespace JSX {\n    interface Element {\n        render(): Element | string | false;\n    }\n}\n\nfunction SFC<T>(_props: Record<string, T>) {\n    return '';\n}\n\n(</*1*/SFC/>);\n(</*2*/SFC<string>/>);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "SFC(_props: Record<string, unknown>): string" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "SFC(_props: Record<string, string>): string" });
            }
            finally
            {
                done();
            }
        });
    }

}
