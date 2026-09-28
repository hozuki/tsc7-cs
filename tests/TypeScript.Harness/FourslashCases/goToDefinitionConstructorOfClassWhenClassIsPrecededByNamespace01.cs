// ported from: internal/fourslash/tests/goToDefinitionConstructorOfClassWhenClassIsPrecededByNamespace01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionConstructorOfClassWhenClassIsPrecededByNamespace01Case
{
    public static void TestGoToDefinitionConstructorOfClassWhenClassIsPrecededByNamespace01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Foo {\n    export var x;\n}\n\nclass Foo {\n    /*definition*/constructor() {\n    }\n}\n\nvar x = new [|/*usage*/Foo|]();"u8);
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
