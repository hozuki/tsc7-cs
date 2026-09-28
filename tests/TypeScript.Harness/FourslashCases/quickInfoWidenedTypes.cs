// ported from: internal/fourslash/tests/quickInfoWidenedTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoWidenedTypesCase
{
    public static void TestQuickInfoWidenedTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nvar /*1*/a = null;                   // var a: any\nvar /*2*/b = undefined;              // var b: any\nvar /*3*/c = { x: 0, y: null };\t// var c: { x: number, y: any }\nvar /*4*/d = [null, undefined];      // var d: any[]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var a: any", "");
                f.VerifyQuickInfoAt(t, "2", "var b: any", "");
                f.VerifyQuickInfoAt(t, "3", "var c: {\n    x: number;\n    y: any;\n}", "");
                f.VerifyQuickInfoAt(t, "4", "var d: any[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
