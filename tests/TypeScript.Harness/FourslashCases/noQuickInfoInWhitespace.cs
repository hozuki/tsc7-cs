// ported from: internal/fourslash/tests/noQuickInfoInWhitespace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NoQuickInfoInWhitespaceCase
{
    public static void TestNoQuickInfoInWhitespace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n/*1*/    private _mspointerupHandler(args) {\n        if (args.button === 3) {\n            return null; \n/*2*/        } else if (args.button === 4) {\n/*3*/            return null;\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyNotQuickInfoExists(t);
                f.GoToMarker(t, "2");
                f.VerifyNotQuickInfoExists(t);
                f.GoToMarker(t, "3");
                f.VerifyNotQuickInfoExists(t);
            }
            finally
            {
                done();
            }
        });
    }

}
