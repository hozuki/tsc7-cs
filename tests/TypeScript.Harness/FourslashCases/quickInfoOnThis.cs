// ported from: internal/fourslash/tests/quickInfoOnThis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnThisCase
{
    public static void TestQuickInfoOnThis(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Restricted {\n    n: number;\n}\nfunction wrapper(wrapped: { (): void; }) { }\nclass Foo {\n    n: number;\n    prop1: th/*0*/is;\n    public explicitThis(this: this) {\n        wrapper(\n            function explicitVoid(this: void) {\n                console.log(th/*1*/is);\n            }\n        )\n        console.log(th/*2*/is);\n    }\n    public explicitInterface(th/*3*/is: Restricted) {\n        console.log(th/*4*/is);\n    }\n    public explicitClass(th/*5*/is: Foo) {\n        console.log(th/*6*/is);\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "0", "this", "");
                f.VerifyQuickInfoAt(t, "1", "this: void", "");
                f.VerifyQuickInfoAt(t, "2", "this: this", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) this: Restricted", "");
                f.VerifyQuickInfoAt(t, "4", "this: Restricted", "");
                f.VerifyQuickInfoAt(t, "5", "(parameter) this: Foo", "");
                f.VerifyQuickInfoAt(t, "6", "this: Foo", "");
            }
            finally
            {
                done();
            }
        });
    }

}
