// ported from: internal/fourslash/tests/instanceTypesForGenericType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InstanceTypesForGenericType1Case
{
    public static void TestInstanceTypesForGenericType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class G<T> {               // Introduce type parameter T\n    self: G<T>;            // Use T as type argument to form instance type\n    f() {\n        this./*1*/self = /*2*/this;  // self and this are both of type G<T>\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) G<T>.self: G<T>", "");
                f.VerifyQuickInfoAt(t, "2", "this: this", "");
            }
            finally
            {
                done();
            }
        });
    }

}
