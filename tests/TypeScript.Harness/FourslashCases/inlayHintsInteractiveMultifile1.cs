// ported from: internal/fourslash/tests/inlayHintsInteractiveMultifile1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveMultifile1Case
{
    public static void TestInlayHintsInteractiveMultifile1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /a.ts\nexport interface Foo { a: string }\n// @Filename: /b.ts\nasync function foo () {\n    return {} as any as import('./a').Foo\n}\nfunction bar () { return import('./a') }\nasync function main () {\n    const a = await foo()\n    const b = await bar()\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayVariableTypeHints = TRI.TSTrue, IncludeInlayFunctionLikeReturnTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
