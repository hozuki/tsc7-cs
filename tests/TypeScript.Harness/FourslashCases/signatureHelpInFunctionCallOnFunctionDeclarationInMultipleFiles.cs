// ported from: internal/fourslash/tests/signatureHelpInFunctionCallOnFunctionDeclarationInMultipleFiles_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpInFunctionCallOnFunctionDeclarationInMultipleFilesCase
{
    public static void TestSignatureHelpInFunctionCallOnFunctionDeclarationInMultipleFiles(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: signatureHelpInFunctionCallOnFunctionDeclarationInMultipleFiles_file0.ts\ndeclare function fn(x: string, y: number);\n// @Filename: signatureHelpInFunctionCallOnFunctionDeclarationInMultipleFiles_file1.ts\ndeclare function fn(x: string);\n// @Filename: signatureHelpInFunctionCallOnFunctionDeclarationInMultipleFiles_file2.ts\nfn(/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
