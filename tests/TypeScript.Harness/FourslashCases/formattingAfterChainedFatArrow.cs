// ported from: internal/fourslash/tests/formattingAfterChainedFatArrow_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingAfterChainedFatArrowCase
{
    public static void TestFormattingAfterChainedFatArrow(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = n => p => {\n    while (true) {\n        void 0;\n    }/**/\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.FormatDocument(t, "");
                f.VerifyCurrentLineContent(t, "    }");
            }
            finally
            {
                done();
            }
        });
    }

}
