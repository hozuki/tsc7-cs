// ported from: internal/fourslash/tests/parameterWithNestedDestructuring_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ParameterWithNestedDestructuringCase
{
    public static void TestParameterWithNestedDestructuring(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[[{ a: 'hello', b: [1] }]]\n  .map(([{ a, b: [c] }]) => /*1*/a + /*2*/c);\nfunction f([[/*3*/a]]: [[string]], { b1: { /*4*/b2 } }: { b1: { b2: string; } }) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) c: number", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) a: string", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) b2: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
