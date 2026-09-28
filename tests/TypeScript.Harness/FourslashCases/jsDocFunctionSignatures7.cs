// ported from: internal/fourslash/tests/jsDocFunctionSignatures7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocFunctionSignatures7Case
{
    public static void TestJsDocFunctionSignatures7(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: Foo.js\n/**\n * @param {string} p0\n * @param {string} [p1]\n */\nfunction Test(p0, p1) {\n    this.P0 = p0;\n    this.P1 = p1;\n}\n\n\nvar /**/test = new Test(\"\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "var test: Test", "");
            }
            finally
            {
                done();
            }
        });
    }

}
