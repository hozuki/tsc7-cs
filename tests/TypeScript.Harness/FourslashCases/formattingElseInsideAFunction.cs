// ported from: internal/fourslash/tests/formattingElseInsideAFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingElseInsideAFunctionCase
{
    public static void TestFormattingElseInsideAFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = function() {\n    if (true) {\n    /*1*/} else {/*2*/\n}\n\n// newline at the end of the file"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "2");
                f.InsertLine(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    } else {");
            }
            finally
            {
                done();
            }
        });
    }

}
