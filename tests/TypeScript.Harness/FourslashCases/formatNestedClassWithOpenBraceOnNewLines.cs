// ported from: internal/fourslash/tests/formatNestedClassWithOpenBraceOnNewLines_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatNestedClassWithOpenBraceOnNewLinesCase
{
    public static void TestFormatNestedClassWithOpenBraceOnNewLines(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("module A\n{\n    class B {\n        /*1*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts168 = f.GetOptions();
                opts168!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForControlBlocks = TRI.TSTrue;
                f.Configure(t, opts168);
                var opts232 = f.GetOptions();
                opts232!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForFunctions = TRI.TSTrue;
                f.Configure(t, opts232);
                f.GoToMarker(t, "1");
                f.Insert(t, "}");
                f.VerifyCurrentFileContent(t, "module A\n{\n    class B\n    {\n    }\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
