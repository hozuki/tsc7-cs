// ported from: internal/fourslash/tests/jsDocPropertyDescription1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocPropertyDescription1Case
{
    public static void TestJsDocPropertyDescription1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface StringExample {\n    /** Something generic */\n    [p: string]: any; \n    /** Something specific */\n    property: number;\n}\nfunction stringExample(e: StringExample) {\n    console.log(e./*property*/property);\n    console.log(e./*string*/anything); \n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "property", "(property) StringExample.property: number", "Something specific");
                f.VerifyQuickInfoAt(t, "string", "(index) StringExample[string]: any", "Something generic");
            }
            finally
            {
                done();
            }
        });
    }

}
