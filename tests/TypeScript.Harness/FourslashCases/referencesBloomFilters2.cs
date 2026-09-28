// ported from: internal/fourslash/tests/referencesBloomFilters2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesBloomFilters2Case
{
    public static void TestReferencesBloomFilters2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: declaration.ts\nvar container = { /*1*/42: 1 };\n// @Filename: expression.ts\nfunction blah() { return (container[42]) === 2;  };\n// @Filename: stringIndexer.ts\nfunction blah2() { container[\"42\"] };\n// @Filename: redeclaration.ts\ncontainer = { \"42\" : 18 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
