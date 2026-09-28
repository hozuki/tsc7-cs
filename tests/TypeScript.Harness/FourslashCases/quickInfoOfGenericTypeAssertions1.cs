// ported from: internal/fourslash/tests/quickInfoOfGenericTypeAssertions1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOfGenericTypeAssertions1Case
{
    public static void TestQuickInfoOfGenericTypeAssertions1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f<T>(x: T): T { return null; }\nvar /*1*/r = <T>(x: T) => x;\nvar /*2*/r2 = < <T>(x: T) => T>f;\nvar a;\nvar /*3*/r3 = < <T>(x: <A>(y: A) => A) => T>a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var r: <T>(x: T) => T", "");
                f.VerifyQuickInfoAt(t, "2", "var r2: <T>(x: T) => T", "");
                f.VerifyQuickInfoAt(t, "3", "var r3: <T>(x: <A>(y: A) => A) => T", "");
            }
            finally
            {
                done();
            }
        });
    }

}
