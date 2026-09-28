// ported from: internal/fourslash/tests/genericParameterHelp_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericParameterHelpCase
{
    public static void TestGenericParameterHelp(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo { }\n\nfunction testFunction<T extends IFoo, U, M extends IFoo>(a: T, b: U, c: M): M {\n    return null;\n}\n\n// Function calls\ntestFunction</*1*/\ntestFunction<any, /*2*/\ntestFunction<any, any, any>(/*3*/\ntestFunction<any, any,/*4*/ any>(null, null, null);\ntestFunction<, ,/*5*/>(null, null, null);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "testFunction<T extends IFoo, U, M extends IFoo>(a: T, b: U, c: M): M", ParameterCount = 3, ParameterName = "T", ParameterSpan = "T extends IFoo" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "U", ParameterSpan = "U" });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "a", ParameterSpan = "a: any" });
                f.GoToMarker(t, "4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "M", ParameterSpan = "M extends IFoo" });
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "M", ParameterSpan = "M extends IFoo" });
            }
            finally
            {
                done();
            }
        });
    }

}
