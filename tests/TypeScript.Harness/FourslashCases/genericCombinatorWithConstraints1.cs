// ported from: internal/fourslash/tests/genericCombinatorWithConstraints1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericCombinatorWithConstraints1Case
{
    public static void TestGenericCombinatorWithConstraints1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function apply<T, U extends Date>(source: T[], selector: (x: T) => U) {\n    var /*1*/xs = source.map(selector); // any[]\n    var /*2*/xs2 = source.map((x: T, a, b): U => { return null }); // any[] \n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(local var) xs: U[]", "");
                f.VerifyQuickInfoAt(t, "2", "(local var) xs2: U[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
