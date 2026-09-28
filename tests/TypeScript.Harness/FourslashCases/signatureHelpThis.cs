// ported from: internal/fourslash/tests/signatureHelpThis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpThisCase
{
    public static void TestSignatureHelpThis(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo<T> {\n    public implicitAny(n: number) {\n    }\n    public explicitThis(this: this, n: number) {\n        console.log(this);\n    }\n    public explicitClass(this: Foo<T>, n: number) {\n        console.log(this);\n    }\n}\n\nfunction implicitAny(x: number): void {\n    return this;\n}\nfunction explicitVoid(this: void, x: number): void {\n    return this;\n}\nfunction explicitLiteral(this: { n: number }, x: number): void {\n    console.log(this);\n}\nlet foo = new Foo<number>();\nfoo.implicitAny(/*1*/);\nfoo.explicitThis(/*2*/);\nfoo.explicitClass(/*3*/);\nimplicitAny(/*4*/12);\nexplicitVoid(/*5*/13);\nlet o = { n: 14, m: explicitLiteral };\no.m(/*6*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "n" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "n" });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "n" });
                f.GoToMarker(t, "4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "x" });
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "x" });
                f.GoToMarker(t, "6");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "x" });
            }
            finally
            {
                done();
            }
        });
    }

}
