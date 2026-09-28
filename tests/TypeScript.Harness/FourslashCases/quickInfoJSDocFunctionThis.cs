// ported from: internal/fourslash/tests/quickInfoJSDocFunctionThis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocFunctionThisCase
{
    public static void TestQuickInfoJSDocFunctionThis(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: Foo.js\n/** @type {function (this: string, string): string} */\nvar f/**/ = function (s) { return s; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "var f: (this: string, arg1: string) => string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
