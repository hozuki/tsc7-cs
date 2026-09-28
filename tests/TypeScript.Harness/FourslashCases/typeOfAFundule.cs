// ported from: internal/fourslash/tests/typeOfAFundule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TypeOfAFunduleCase
{
    public static void TestTypeOfAFundule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function m1() { return 1; }\nnamespace m1 { export var y = 2; }\nfunction foo13() {\n    return m1;\n}\nvar /**/r13 = foo13();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "var r13: typeof m1", "");
            }
            finally
            {
                done();
            }
        });
    }

}
