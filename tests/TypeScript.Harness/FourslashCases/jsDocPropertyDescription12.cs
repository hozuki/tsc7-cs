// ported from: internal/fourslash/tests/jsDocPropertyDescription12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription12Case
{
    public static void TestJsDocPropertyDescription12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type SymbolAlias = {\n    /** Something generic */\n    [p: symbol]: string;\n}\nfunction symbolAlias(e: SymbolAlias) {\n    console.log(e./*symbolAlias*/anything);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "symbolAlias", "any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
