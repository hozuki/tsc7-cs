// ported from: internal/fourslash/tests/underscoreTypings02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnderscoreTypings02Case
{
    public static void TestUnderscoreTypings02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @module: CommonJS\ninterface Dictionary<T> {\n    [x: string]: T;\n}\nexport interface ChainedObject<T> {\n    functions: ChainedArray<string>;\n    omit(): ChainedObject<T>;\n    clone(): ChainedObject<T>;\n}\ninterface ChainedDictionary<T> extends ChainedObject<Dictionary<>> {\n    foldl(): ChainedObject<T>;\n    clone(): ChainedDictionary<T>;\n}\nexport interface ChainedArray<T> extends ChainedObject<Array<T>> {\n    groupBy(): ChainedDictionary<any[]>;\n    groupBy(propertyName): ChainedDictionary<any[]>;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToPosition(t, 0);
                f.VerifyNumberOfErrorsInCurrentFile(t, 2);
            }
            finally
            {
                done();
            }
        });
    }

}
