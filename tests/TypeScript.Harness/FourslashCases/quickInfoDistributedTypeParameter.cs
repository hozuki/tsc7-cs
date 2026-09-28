// ported from: internal/fourslash/tests/quickInfoDistributedTypeParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDistributedTypeParameterCase
{
    public static void TestQuickInfoDistributedTypeParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Conditional<T> =\n    T/*check*/ extends T/*extends*/\n        ? T/*trueType*/\n        : T/*falseType*/;\n\ntype NonDistributed<T> = [T/*nonDistributed*/] extends [unknown] ? T : never;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "check", "(type parameter) (distributed) T in type Conditional<T>", "");
                f.VerifyQuickInfoAt(t, "extends", "(type parameter) (distributed) T in type Conditional<T>", "");
                f.VerifyQuickInfoAt(t, "trueType", "(type parameter) (distributed) T in type Conditional<T>", "");
                f.VerifyQuickInfoAt(t, "falseType", "(type parameter) (distributed) T in type Conditional<T>", "");
                f.VerifyQuickInfoAt(t, "nonDistributed", "(type parameter) T in type NonDistributed<T>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
