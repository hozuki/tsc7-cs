// ported from: internal/fourslash/tests/goToDefinitionObjectLiteralProperties2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionObjectLiteralProperties2Case
{
    public static void TestGoToDefinitionObjectLiteralProperties2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type C = {\n  foo: string;\n  bar: number;\n};\n\ndeclare function fn<T extends C>(arg: T): T;\n\nfn({\n  foo/*1*/: \"\",\n  bar/*2*/: true,\n});\n\nconst result = fn({\n  foo/*3*/: \"\",\n  bar/*4*/: 1,\n});\n\n// this one shouldn't go to the constraint type\nresult.foo/*5*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1", "2", "3", "4", "5");
            }
            finally
            {
                done();
            }
        });
    }

}
