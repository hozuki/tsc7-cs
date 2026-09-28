// ported from: internal/fourslash/tests/quickInfoInFunctionTypeReference2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoInFunctionTypeReference2Case
{
    public static void TestQuickInfoInFunctionTypeReference2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C<T> {\n    map(fn: (/*1*/k: string, /*2*/value: T, context: any) => void, context: any) {\n    }\n}\nvar c: C<number>;\nc.map(/*3*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) k: string", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) value: T", "");
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "map(fn: (k: string, value: number, context: any) => void, context: any): void" });
            }
            finally
            {
                done();
            }
        });
    }

}
