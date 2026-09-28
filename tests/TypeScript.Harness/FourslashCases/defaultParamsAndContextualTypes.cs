// ported from: internal/fourslash/tests/defaultParamsAndContextualTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DefaultParamsAndContextualTypesCase
{
    public static void TestDefaultParamsAndContextualTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface FooOptions {\n    text?: string;\n}\ninterface Foo {\n    bar(xy: string, options?: FooOptions): void;\n}\nvar o: Foo = {\n    bar: function (x/*1*/y, opt/*2*/ions = {}) {\n        // expect xy to have type string, and options to have type FooOptions in here\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) xy: string", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) options: FooOptions", "");
            }
            finally
            {
                done();
            }
        });
    }

}
