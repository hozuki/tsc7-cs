// ported from: internal/fourslash/tests/goToDefinitionUnionTypeProperty4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionUnionTypeProperty4Case
{
    public static void TestGoToDefinitionUnionTypeProperty4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface SnapCrackle {\n    /*def1*/pop(): string;\n}\n\ninterface Magnitude {\n    /*def2*/pop(): number;\n}\n\ninterface Art {\n    /*def3*/pop(): boolean;\n}\n\nvar art: Art;\nvar magnitude: Magnitude;\nvar snapcrackle: SnapCrackle;\n\nvar x = (snapcrackle || magnitude || art).[|/*usage*/pop|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "usage");
            }
            finally
            {
                done();
            }
        });
    }

}
