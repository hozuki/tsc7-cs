// ported from: internal/fourslash/tests/inlayHintsInteractiveVariableTypes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveVariableTypes2Case
{
    public static void TestInlayHintsInteractiveVariableTypes2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const object = { foo: 1, bar: 2 }\nconst array = [1, 2]\nconst a = object;\nconst { foo, bar } = object;\nconst {} = object;\nconst b = array;\nconst [ first, second ] = array;\nconst [] = array;\ndeclare function foo<T extends number>(t: T): T\nconst x = foo(1)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayVariableTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
