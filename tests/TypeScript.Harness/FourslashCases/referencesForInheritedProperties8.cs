// ported from: internal/fourslash/tests/referencesForInheritedProperties8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForInheritedProperties8Case
{
    public static void TestReferencesForInheritedProperties8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface C extends D {\n    /*d*/propD: number;\n}\ninterface D extends C {\n    propD: string;\n    /*c*/propC: number;\n}\nvar d: D;\nd.propD;\nd.propC;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "d", "c");
            }
            finally
            {
                done();
            }
        });
    }

}
