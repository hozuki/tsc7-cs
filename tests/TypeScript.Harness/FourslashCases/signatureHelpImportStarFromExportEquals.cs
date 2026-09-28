// ported from: internal/fourslash/tests/signatureHelpImportStarFromExportEquals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpImportStarFromExportEqualsCase
{
    public static void TestSignatureHelpImportStarFromExportEquals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /node_modules/@types/abs/index.d.ts\ndeclare function abs(str: string): string;\nexport = abs;\n// @Filename: /a.js\nimport * as abs from \"abs\";\nabs.default/**/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "(");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "default(str: string): string" });
            }
            finally
            {
                done();
            }
        });
    }

}
