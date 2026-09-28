// ported from: internal/fourslash/tests/jsDocPropertyDescription3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription3Case
{
    public static void TestJsDocPropertyDescription3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface LiteralExample {\n    /** Something generic */\n    [key: `data-${string}`]: string;\n     /** Something else */\n    [key: `prefix${number}`]: number;\n}\nfunction literalExample(e: LiteralExample) {\n    console.log(e./*literal*/anything);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "literal", "any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
