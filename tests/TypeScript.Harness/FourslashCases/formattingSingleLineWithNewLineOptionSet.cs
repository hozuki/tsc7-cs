// ported from: internal/fourslash/tests/formattingSingleLineWithNewLineOptionSet_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingSingleLineWithNewLineOptionSetCase
{
    public static void TestFormattingSingleLineWithNewLineOptionSet(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/namespace Default{}\n/*2*/function foo(){}\n/*3*/if (true){}\n/*4*/function boo() {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts211 = f.GetOptions();
                opts211!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForFunctions = TRI.TSTrue;
                f.Configure(t, opts211);
                var opts279 = f.GetOptions();
                opts279!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForControlBlocks = TRI.TSTrue;
                f.Configure(t, opts279);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "namespace Default { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "function foo() { }");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "if (true) { }");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "function boo()");
            }
            finally
            {
                done();
            }
        });
    }

}
