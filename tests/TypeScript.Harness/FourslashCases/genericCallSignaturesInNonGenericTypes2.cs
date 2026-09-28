// ported from: internal/fourslash/tests/genericCallSignaturesInNonGenericTypes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericCallSignaturesInNonGenericTypes2Case
{
    public static void TestGenericCallSignaturesInNonGenericTypes2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface WrappedArray<T> { }\ninterface Underscore {\n    <T>(list: T[]): WrappedArray<T>;\n}\nvar _: Underscore;\nvar a: number[];\nvar /**/b = _(a);  // WrappedArray<any>, should be WrappedArray<number>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "var b: WrappedArray<number>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
