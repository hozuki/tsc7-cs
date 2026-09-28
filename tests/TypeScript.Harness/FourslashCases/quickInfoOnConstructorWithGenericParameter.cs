// ported from: internal/fourslash/tests/quickInfoOnConstructorWithGenericParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnConstructorWithGenericParameterCase
{
    public static void TestQuickInfoOnConstructorWithGenericParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    x: number;\n}\nclass Foo<T> {\n    y: T;\n}\nclass A {\n    foo() { }\n}\nclass B extends A {\n    constructor(a: Foo<I>, b: number) {\n        super();\n    }\n}\nvar x = new /*2*/B(/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B(a: Foo<I>, b: number): B" });
                f.Insert(t, "null,");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B(a: Foo<I>, b: number): B" });
                f.Insert(t, "10);");
                f.VerifyQuickInfoAt(t, "2", "constructor B(a: Foo<I>, b: number): B", "");
            }
            finally
            {
                done();
            }
        });
    }

}
