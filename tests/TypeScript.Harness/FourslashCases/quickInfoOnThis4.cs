// ported from: internal/fourslash/tests/quickInfoOnThis4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnThis4Case
{
    public static void TestQuickInfoOnThis4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface ContextualInterface {\n    m: number;\n    method(this: this, n: number);\n}\nlet o: ContextualInterface = {\n    m: 12,\n    method(n) {\n        let x = this/*1*/.m;\n    }\n}\ninterface ContextualInterface2 {\n    (this: void, n: number): void;\n}\nlet contextualInterface2: ContextualInterface2 = function (th/*2*/is, n) { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "this: ContextualInterface", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) this: void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
