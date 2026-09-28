// ported from: internal/fourslash/tests/genericFunctionSignatureHelp3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericFunctionSignatureHelp3Case
{
    public static void TestGenericFunctionSignatureHelp3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo1<T>(x: number, callback: (y1: T) => number) { }\nfunction foo2<T>(x: number, callback: (y2: T) => number) { }\nfunction foo3<T>(x: number, callback: (y3: T) => number) { }\nfunction foo4<T>(x: number, callback: (y4: T) => number) { }\nfunction foo5<T>(x: number, callback: (y5: T) => number) { }\nfunction foo6<T>(x: number, callback: (y6: T) => number) { }\nfunction foo7<T>(x: number, callback: (y7: T) => number) { }\n IDE shows the results on the right of each line, fourslash says different\nfoo1(/*1*/               // signature help shows y as T\nfoo2(1,/*2*/             // signature help shows y as {}\nfoo3(1, (/*3*/           // signature help shows y as T\nfoo4<string>(1,/*4*/     // signature help shows y as string\nfoo5<string>(1, (/*5*/   // signature help shows y as T\nfoo6(1, </*6*/           // signature help shows y as {}\nfoo7(1, <string>(/*7*/   // signature help shows y as T"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo1(x: number, callback: (y1: unknown) => number): void" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo2(x: number, callback: (y2: unknown) => number): void" });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callback(y3: unknown): number" });
                f.GoToMarker(t, "4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo4(x: number, callback: (y4: string) => number): void" });
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callback(y5: string): number" });
                f.GoToMarker(t, "6");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo6(x: number, callback: (y6: unknown) => number): void" });
                f.Insert(t, "string>(null,null);");
                f.GoToMarker(t, "7");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo7(x: number, callback: (y7: unknown) => number): void" });
            }
            finally
            {
                done();
            }
        });
    }

}
