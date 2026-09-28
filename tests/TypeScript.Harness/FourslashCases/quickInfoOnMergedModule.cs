// ported from: internal/fourslash/tests/quickInfoOnMergedModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnMergedModuleCase
{
    public static void TestQuickInfoOnMergedModule(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nnamespace M2 {\n    export interface A {\n        foo: string;\n    }\n    var a: A;\n    var r = a.foo + a.bar;\n}\nnamespace M2 {\n    export interface A {\n        bar: number;\n    }\n    var a: A;\n    var r = a.fo/*1*/o + a.bar;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) M2.A.foo: string", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
