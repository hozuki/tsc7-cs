// ported from: internal/fourslash/tests/formatIfTryCatchBlocks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatIfTryCatchBlocksCase
{
    public static void TestFormatIfTryCatchBlocks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("try {\n}\ncatch {\n}\n\ntry {\n}\ncatch (e) {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts187 = f.GetOptions();
                opts187!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForControlBlocks = TRI.TSTrue;
                f.Configure(t, opts187);
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "try\n{\n}\ncatch\n{\n}\n\ntry\n{\n}\ncatch (e)\n{\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
