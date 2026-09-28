// ported from: internal/fourslash/tests/jsDocPropertyDescription4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription4Case
{
    public static void TestJsDocPropertyDescription4(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface MultipleExample {\n    /** Something generic */\n    [key: string | number | symbol]: string;\n}\nfunction multipleExample(e: MultipleExample) {\n    console.log(e./*multiple*/anything);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "multiple", "(index) MultipleExample[string | number | symbol]: string", "Something generic");
            }
            finally
            {
                done();
            }
        });
    }

}
