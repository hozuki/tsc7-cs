// ported from: internal/fourslash/tests/semanticModernClassificationInfinityAndNaN_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationInfinityAndNaNCase
{
    public static void TestSemanticModernClassificationInfinityAndNaN(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" Infinity;\n NaN;\n\n// Regular properties\n\nconst obj1 = {\n    Infinity: 100,\n    NaN: 200,\n    \"-Infinity\": 300\n};\n\nobj1.Infinity;\nobj1.NaN;\nobj1[\"-Infinity\"];\n\n// Shorthand properties\n\nconst obj2 = {\n    Infinity,\n    NaN,\n}\n\nobj2.Infinity;\nobj2.NaN;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable.declaration.readonly", Text = "obj1" }, new F.SemanticToken { Type = "variable.readonly", Text = "obj1" }, new F.SemanticToken { Type = "variable.readonly", Text = "obj1" }, new F.SemanticToken { Type = "variable.readonly", Text = "obj1" }, new F.SemanticToken { Type = "variable.declaration.readonly", Text = "obj2" }, new F.SemanticToken { Type = "variable.readonly", Text = "obj2" }, new F.SemanticToken { Type = "variable.readonly", Text = "obj2" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
