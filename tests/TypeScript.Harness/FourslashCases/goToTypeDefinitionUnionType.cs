// ported from: internal/fourslash/tests/goToTypeDefinitionUnionType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinitionUnionTypeCase
{
    public static void TestGoToTypeDefinitionUnionType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "class /*definition0*/C {\n    p;\n}\n\ninterface /*definition1*/I {\n    x;\n}\n\nnamespace M {\n    export interface /*definition2*/I {\n        y;\n    }\n}\n\nvar x: C | I | M.I;\n\n/*reference*/x;";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference");
            }
            finally
            {
                done();
            }
        });
    }

}
