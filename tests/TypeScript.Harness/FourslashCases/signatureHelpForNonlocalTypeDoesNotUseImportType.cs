// ported from: internal/fourslash/tests/signatureHelpForNonlocalTypeDoesNotUseImportType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpForNonlocalTypeDoesNotUseImportTypeCase
{
    public static void TestSignatureHelpForNonlocalTypeDoesNotUseImportType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: exporter.ts\nexport interface Thing {}\nexport const Foo: () => Thing = null as any;\n// @Filename: usage.ts\nimport {Foo} from \"./exporter\"\nfunction f(p = Foo()): void {}\nf(/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(p?: Thing): void" });
            }
            finally
            {
                done();
            }
        });
    }

}
