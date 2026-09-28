// ported from: internal/fourslash/tests/multilineCommentBeforeOpenBrace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MultilineCommentBeforeOpenBraceCase
{
    public static void TestMultilineCommentBeforeOpenBrace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function test() /*1*//* %^ */\n{\n    if (true) /*2*//* %^ */\n    {\n    }\n}\nfunction a() {\n    /* %^ */ }/*3*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "function test() /* %^ */ {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    if (true) /* %^ */ {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
