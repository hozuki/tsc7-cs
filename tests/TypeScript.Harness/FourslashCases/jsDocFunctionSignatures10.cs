// ported from: internal/fourslash/tests/jsDocFunctionSignatures10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocFunctionSignatures10Case
{
    public static void TestJsDocFunctionSignatures10(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: Foo.js\n/**\n * Do some foo things\n * @template T A Foolish template\n * @param {T} x a parameter\n */\nfunction foo(x) {\n}\n\nfo/**/o()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "function foo<any>(x: any): void", "Do some foo things");
            }
            finally
            {
                done();
            }
        });
    }

}
