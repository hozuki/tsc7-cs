// ported from: internal/fourslash/tests/jsDocPropertyDescription11_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription11Case
{
    public static void TestJsDocPropertyDescription11(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type AliasExample = {\n    /** Something generic */\n    [p: string]: string;\n    /** Something else */\n    [key: `any${string}`]: string;\n}\nfunction aliasExample(e: AliasExample) {\n    console.log(e./*alias*/anything);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "alias", "(index) AliasExample[string | `any${string}`]: string", "Something generic\nSomething else");
            }
            finally
            {
                done();
            }
        });
    }

}
