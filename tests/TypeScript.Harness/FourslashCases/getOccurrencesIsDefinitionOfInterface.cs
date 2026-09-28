// ported from: internal/fourslash/tests/getOccurrencesIsDefinitionOfInterface_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesIsDefinitionOfInterfaceCase
{
    public static void TestGetOccurrencesIsDefinitionOfInterface(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/interface /*2*/I {\n    p: number;\n}\nlet i: /*3*/I = { p: 12 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
