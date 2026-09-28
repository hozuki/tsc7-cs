// ported from: internal/fourslash/tests/signatureHelpExpandedRestTuples_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpExpandedRestTuplesCase
{
    public static void TestSignatureHelpExpandedRestTuples(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export function complex(item: string, another: string, ...rest: [] | [settings: object, errorHandler: (err: Error) => void] | [errorHandler: (err: Error) => void, ...mixins: object[]]) {\n    \n}\n\ncomplex(/*1*/);\ncomplex(\"ok\", \"ok\", /*2*/);\ncomplex(\"ok\", \"ok\", e => void e, {}, /*3*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "complex(item: string, another: string): void", ParameterCount = 2, ParameterName = "item", ParameterSpan = "item: string", OverloadsCount = 3, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "complex(item: string, another: string, settings: object, errorHandler: (err: Error) => void): void", ParameterCount = 4, ParameterName = "settings", ParameterSpan = "settings: object", OverloadsCount = 3, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "complex(item: string, another: string, errorHandler: (err: Error) => void, ...mixins: object[]): void", OverloadsCount = 3, IsVariadic = true, IsVariadicSet = true });
            }
            finally
            {
                done();
            }
        });
    }

}
