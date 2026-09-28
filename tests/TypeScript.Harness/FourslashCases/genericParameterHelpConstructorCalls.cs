// ported from: internal/fourslash/tests/genericParameterHelpConstructorCalls_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericParameterHelpConstructorCallsCase
{
    public static void TestGenericParameterHelpConstructorCalls(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo { }\n\nclass testClass<T extends IFoo, U, M extends IFoo> {\n    constructor(a:T, b:U, c:M){ }\n}\n\n// Constructor calls\nnew testClass</*constructor1*/\nnew testClass<IFoo, /*constructor2*/\nnew testClass</*constructor3*/>(null, null, null)\nnew testClass<,,/*constructor4*/>(null, null, null)\nnew testClass<IFoo,/*constructor5*/IFoo,IFoo>(null, null, null)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "constructor1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "testClass<T extends IFoo, U, M extends IFoo>(a: T, b: U, c: M): testClass<T, U, M>", ParameterName = "T", ParameterSpan = "T extends IFoo" });
                f.GoToMarker(t, "constructor2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "U", ParameterSpan = "U" });
                f.GoToMarker(t, "constructor3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "T", ParameterSpan = "T extends IFoo" });
                f.GoToMarker(t, "constructor4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "M", ParameterSpan = "M extends IFoo" });
                f.GoToMarker(t, "constructor5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "U", ParameterSpan = "U" });
            }
            finally
            {
                done();
            }
        });
    }

}
