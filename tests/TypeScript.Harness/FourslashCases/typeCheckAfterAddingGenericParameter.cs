// ported from: internal/fourslash/tests/typeCheckAfterAddingGenericParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TypeCheckAfterAddingGenericParameterCase
{
    public static void TestTypeCheckAfterAddingGenericParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f<x, x>() { }\nfunction f2<X, X>(b: X): X { return null; }\nclass C<X> {\n    public f<x, x>() {}\nf2<X>(b): X { return null; }\n}\n\ninterface I<X, X> {\n    f<X/*addTypeParam*/>();\n    f2<X>(/*addParam*/a: X): X;\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "addParam");
                f.Insert(t, ", X");
                f.GoToMarker(t, "addTypeParam");
                f.Insert(t, ", X");
            }
            finally
            {
                done();
            }
        });
    }

}
