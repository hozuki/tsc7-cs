// ported from: internal/fourslash/tests/goToDefinitionConstructorOfClassExpression01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionConstructorOfClassExpression01Case
{
    public static void TestGoToDefinitionConstructorOfClassExpression01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = class C {\n    /*definition*/constructor() {\n        var other = new [|/*xusage*/C|];\n    }\n}\n\nvar y = class C extends x {\n    constructor() {\n        super();\n        var other = new [|/*yusage*/C|];\n    }\n}\nvar z = class C extends x {\n    m() {\n        return new [|/*zusage*/C|];\n    }\n}\n\nvar x1 = new [|/*cref*/C|]();\nvar x2 = new [|/*xref*/x|]();\nvar y1 = new [|/*yref*/y|]();\nvar z1 = new [|/*zref*/z|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "xusage", "yusage", "zusage", "cref", "xref", "yref", "zref");
            }
            finally
            {
                done();
            }
        });
    }

}
