// ported from: internal/fourslash/tests/isDefinitionInterfaceImplementation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IsDefinitionInterfaceImplementationCase
{
    public static void TestIsDefinitionInterfaceImplementation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    /*1*/M(): void;\n}\n\nclass C implements I {\n    /*2*/M() { }\n}\n\n({} as I).M();\n({} as C).M();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
