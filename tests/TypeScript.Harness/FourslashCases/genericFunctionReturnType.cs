// ported from: internal/fourslash/tests/genericFunctionReturnType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericFunctionReturnTypeCase
{
    public static void TestGenericFunctionReturnType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo<T, U>(x: T, y: U): (a: U) => T {\n    var z = y;\n    return (z) => x;\n}\nvar /*2*/r = foo(/*1*/1, \"\");\nvar /*4*/r2 = r(/*3*/\"\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(x: number, y: string): (a: string) => number" });
                f.VerifyQuickInfoAt(t, "2", "var r: (a: string) => number", "");
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "r(a: string): number" });
                f.VerifyQuickInfoAt(t, "4", "var r2: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
