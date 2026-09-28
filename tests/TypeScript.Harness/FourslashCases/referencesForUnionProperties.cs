// ported from: internal/fourslash/tests/referencesForUnionProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForUnionPropertiesCase
{
    public static void TestReferencesForUnionProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface One {\n    common: { /*one*/a: number; };\n}\n\ninterface Base {\n    /*base*/a: string;\n    b: string;\n}\n\ninterface HasAOrB extends Base {\n    a: string;\n    b: string;\n}\n\ninterface Two {\n    common: HasAOrB;\n}\n\nvar x : One | Two;\n\nx.common./*x*/a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "one", "base", "x");
            }
            finally
            {
                done();
            }
        });
    }

}
