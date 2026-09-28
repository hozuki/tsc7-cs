// ported from: internal/fourslash/tests/jsDocPropertyDescription8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription8Case
{
    public static void TestJsDocPropertyDescription8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class SymbolClass {\n    /** Something generic */\n    static [p: symbol]: any;\n}\nfunction symbolClass(e: typeof SymbolClass) {\n    console.log(e./*symbolClass*/anything);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "symbolClass", "any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
