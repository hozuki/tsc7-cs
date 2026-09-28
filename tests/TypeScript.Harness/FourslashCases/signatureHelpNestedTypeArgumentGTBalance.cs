// ported from: internal/fourslash/tests/signatureHelpNestedTypeArgumentGTBalance_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpNestedTypeArgumentGTBalanceCase
{
    public static void TestSignatureHelpNestedTypeArgumentGTBalance(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function f<T, U>(): void;\ntype A<T> = T;\ntype B<T> = T;\ntype C<T> = T;\nf<A<B<C<number>>>, /*nested*/;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "nested");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f<T, U>(): void", ParameterName = "U", ParameterSpan = "U", ParameterCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
