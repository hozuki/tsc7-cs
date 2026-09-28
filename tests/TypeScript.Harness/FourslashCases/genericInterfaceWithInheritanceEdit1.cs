// ported from: internal/fourslash/tests/genericInterfaceWithInheritanceEdit1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericInterfaceWithInheritanceEdit1Case
{
    public static void TestGenericInterfaceWithInheritanceEdit1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface ChainedObject<T> {\n    values(): ChainedArray<any>;\n    pairs(): ChainedArray<any[]>;\n    extend(...sources: any[]): ChainedObject<T>;\n\n    value(): T;\n}\ninterface ChainedArray<T> extends ChainedObject<Array<T>> {\n\n    extend(...sources: any[]): ChainedArray<T>;\n}\n /*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "1");
                f.Insert(t, " ");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
