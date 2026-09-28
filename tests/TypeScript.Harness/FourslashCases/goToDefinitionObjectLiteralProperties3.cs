// ported from: internal/fourslash/tests/goToDefinitionObjectLiteralProperties3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionObjectLiteralProperties3Case
{
    public static void TestGoToDefinitionObjectLiteralProperties3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type A = {\n  foo: unknown;\n};\n\ntype B = {\n  foo?: unknown;\n  bar: unknown;\n};\n\nfunction test1(arg: A | B) {}\n\ntest1({\n  foo/*1*/: 1,\n});\n\nfunction test2<T extends A>(arg: T | B) {}\n\ntest2({\n  foo/*2*/: 2,\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
