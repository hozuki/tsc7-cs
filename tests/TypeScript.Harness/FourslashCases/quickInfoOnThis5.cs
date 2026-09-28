// ported from: internal/fourslash/tests/quickInfoOnThis5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnThis5Case
{
    public static void TestQuickInfoOnThis5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitThis: true\nconst foo = {\n    num: 0,\n    f() {\n        type Y = typeof th/*1*/is;\n        type Z = typeof th/*2*/is.num;\n    },\n    g(this: number) {\n        type X = typeof th/*3*/is;\n    }\n}\nclass Foo {\n    num = 0;\n    f() {\n        type Y = typeof th/*4*/is;\n        type Z = typeof th/*5*/is.num;\n    }\n    g(this: number) {\n        type X = typeof th/*6*/is;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
