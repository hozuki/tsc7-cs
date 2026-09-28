// ported from: internal/fourslash/tests/jsDocPropertyDescription9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription9Case
{
    public static void TestJsDocPropertyDescription9(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class LiteralClass {\n    /** Something generic */\n    static [key: `prefix${string}`]: any;\n    /** Something else */\n    static [key: `prefix${number}`]: number;\n}\nfunction literalClass(e: typeof LiteralClass) {\n    console.log(e./*literal1Class*/prefixMember); \n    console.log(e./*literal2Class*/anything);\n    console.log(e./*literal3Class*/prefix0);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "literal1Class", "(index) LiteralClass[`prefix${string}`]: any", "Something generic");
                f.VerifyQuickInfoAt(t, "literal2Class", "any", "");
                f.VerifyQuickInfoAt(t, "literal3Class", "(index) LiteralClass[`prefix${string}` | `prefix${number}`]: any", "Something generic\nSomething else");
            }
            finally
            {
                done();
            }
        });
    }

}
