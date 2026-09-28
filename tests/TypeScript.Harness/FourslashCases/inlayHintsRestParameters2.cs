// ported from: internal/fourslash/tests/inlayHintsRestParameters2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsRestParameters2Case
{
    public static void TestInlayHintsRestParameters2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(a: unknown, b: unknown, c: unknown) { }\nfunction foo1(...x: [number, number | undefined]) {\n    foo(...x, 3);\n}\nfunction foo2(...x: []) {\n    foo(...x, 1, 2, 3);\n}\nfunction foo3(...x: [number, number?]) {\n    foo(1, ...x);\n}\nfunction foo4(...x: [number, number?]) {\n    foo(...x, 3);\n}\nfunction foo5(...x: [number, number]) {\n    foo(...x, 3);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsAll } });
            }
            finally
            {
                done();
            }
        });
    }

}
