// ported from: internal/fourslash/tests/jsDocPropertyDescription6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription6Case
{
    public static void TestJsDocPropertyDescription6(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Literal1Example {\n    [key: `prefix${string}`]: number | string;\n    /** Something else */\n    [key: `prefix${number}`]: number;\n}\nfunction literal1Example(e: Literal1Example) {\n    console.log(e./*literal1*/prefixMember);\n    console.log(e./*literal2*/anything);\n    console.log(e./*literal3*/prefix0);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "literal1", "(index) Literal1Example[`prefix${string}`]: string | number", "");
                f.VerifyQuickInfoAt(t, "literal2", "any", "");
                f.VerifyQuickInfoAt(t, "literal3", "(index) Literal1Example[`prefix${string}` | `prefix${number}`]: number", "Something else");
            }
            finally
            {
                done();
            }
        });
    }

}
