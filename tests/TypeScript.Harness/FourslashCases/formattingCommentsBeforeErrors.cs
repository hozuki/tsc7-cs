// ported from: internal/fourslash/tests/formattingCommentsBeforeErrors_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingCommentsBeforeErrorsCase
{
    public static void TestFormattingCommentsBeforeErrors(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace A {\n    interface B {\n        // a\n        // b\n        baz();\n/*0*/        // d /*1*/asd a\n        // e\n        foo();\n        // f asd\n        // g as\n        bar();\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "\n");
                f.GoToMarker(t, "0");
                f.VerifyCurrentLineContent(t, "        // d ");
            }
            finally
            {
                done();
            }
        });
    }

}
