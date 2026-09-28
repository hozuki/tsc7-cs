// ported from: internal/fourslash/tests/quickInfoInObjectLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoInObjectLiteralCase
{
    public static void TestQuickInfoInObjectLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    doStuff(x: string, callback: (a: string) => string);\n}\nvar x1: Foo = {\n    y/*1*/1: () => {\n        return \"\";\n    } ,\n    doStuff: (z, callback) => { return callback(this.y); }\n}\nvar value = 3;\nclass Foo {\n    static getRandomPosition() {\n        return {\n            \"row\": v/*2*/alue\n        }\n  }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) y1: () => string", "");
                f.VerifyQuickInfoAt(t, "2", "var value: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
