// ported from: internal/fourslash/tests/quickInfoTypeArgumentInferenceWithMethodWithoutBody_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoTypeArgumentInferenceWithMethodWithoutBodyCase
{
    public static void TestQuickInfoTypeArgumentInferenceWithMethodWithoutBody(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface ProxyHandler<T extends object> {\n    getPrototypeOf?(target: T): object | null;\n}\ninterface ProxyConstructor {\n    new <T extends object>(target: T, handler: ProxyHandler<T>): T;\n}\ndeclare var Proxy: ProxyConstructor;\nlet target = {}\nlet proxy = new /**/Proxy(target, {\n    getPrototypeOf()\n})"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoExists(t);
            }
            finally
            {
                done();
            }
        });
    }

}
