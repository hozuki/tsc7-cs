// ported from: internal/fourslash/tests/inlayHintsOverloadCall1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsOverloadCall1Case
{
    public static void TestInlayHintsOverloadCall1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Call {\n    (a: number): void\n    (b: number, c: number): void\n    new (d: number): Call\n}\ndeclare const call: Call;\ncall(1);\ncall(1, 2);\nnew call(1);\ndeclare function foo(w: number): void\ndeclare function foo(a: number, b: number): void;\ndeclare function foo(a: number | undefined, b: number | undefined): void;\nfoo(1)\nfoo(1, 2)\nclass Class {\n    constructor(a: number);\n    constructor(b: number, c: number);\n    constructor(b: number, c?: number) { }\n}\nnew Class(1)\nnew Class(1, 2)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsLiterals } });
            }
            finally
            {
                done();
            }
        });
    }

}
