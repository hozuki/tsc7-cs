// ported from: internal/fourslash/tests/jsDocPropertyDescription5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription5Case
{
    public static void TestJsDocPropertyDescription5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Multiple1Example {\n    /** Something generic */\n    [key: number | symbol | `data-${string}` | `data-${number}`]: string;\n}\nfunction multiple1Example(e: Multiple1Example) {\n    console.log(e./*multiple1*/anything);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "multiple1", "any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
