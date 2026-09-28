// ported from: internal/fourslash/tests/quickInfoModuleVariables_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoModuleVariablesCase
{
    public static void TestQuickInfoModuleVariables(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = 1;\nnamespace M {\n    export var x = 2;\n    console.log(/*1*/x); // 2\n}\nnamespace M {\n    console.log(/*2*/x); // 2\n}\nnamespace M {\n    var x = 3;\n    console.log(/*3*/x); // 3\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var M.x: number", "");
                f.VerifyQuickInfoAt(t, "2", "var M.x: number", "");
                f.VerifyQuickInfoAt(t, "3", "var x: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
