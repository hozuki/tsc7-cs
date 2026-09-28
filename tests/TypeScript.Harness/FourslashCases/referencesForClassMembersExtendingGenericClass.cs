// ported from: internal/fourslash/tests/referencesForClassMembersExtendingGenericClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForClassMembersExtendingGenericClassCase
{
    public static void TestReferencesForClassMembersExtendingGenericClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base<T> {\n    /*a1*/a: this;\n    /*method1*/method<U>(a?:T, b?:U): this { }\n}\nclass MyClass extends Base<number> {\n    /*a2*/a;\n    /*method2*/method() { }\n}\n\nvar c: MyClass;\nc./*a3*/a;\nc./*method3*/method();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "a1", "a2", "a3", "method1", "method2", "method3");
            }
            finally
            {
                done();
            }
        });
    }

}
