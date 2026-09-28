// ported from: internal/fourslash/tests/formattingInComment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingInCommentCase
{
    public static void TestFormattingInComment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\nfoo(              ); // /*1*/\n}\nfunction foo() {       var x;       } // /*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, ";");
                f.VerifyCurrentLineContent(t, "foo(              ); // ;");
                f.GoToMarker(t, "2");
                f.Insert(t, "}");
                f.VerifyCurrentLineContent(t, "function foo() {       var x;       } // }");
            }
            finally
            {
                done();
            }
        });
    }

}
