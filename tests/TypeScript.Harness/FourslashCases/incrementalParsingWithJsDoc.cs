// ported from: internal/fourslash/tests/incrementalParsingWithJsDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IncrementalParsingWithJsDocCase
{
    public static void TestIncrementalParsingWithJsDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|import a from 'a/aaaaaaa/aaaaaaa/aaaaaa/aaaaaaa';\n/**/import b from 'b';\nimport c from 'c';|]\n[|/** @internal */|]\nexport class LanguageIdentifier[| { }|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
                f.GoToMarker(t, "");
                f.Backspace(t, 1);
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
