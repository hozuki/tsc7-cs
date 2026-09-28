// ported from: internal/fourslash/tests/inlayHintsInteractiveFunctionParameterTypes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveFunctionParameterTypes2Case
{
    public static void TestInlayHintsInteractiveFunctionParameterTypes2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {}\nnamespace N { export class Foo {} }\ninterface Foo {}\nfunction f1(a = 1) {}\nfunction f2(a = \"a\") {}\nfunction f3(a = true) {}\nfunction f4(a = { } as Foo) {}\nfunction f5(a = <Foo>{}) {}\nfunction f6(a = {} as const) {}\nfunction f7(a = (({} as const))) {}\nfunction f8(a = new C()) {}\nfunction f9(a = new N.C()) {}\nfunction f10(a = ((((new C()))))) {}\nfunction f11(a = { a: 1, b: 1 }) {}\nfunction f12(a = ((({ a: 1, b: 1 })))) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayFunctionParameterTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
