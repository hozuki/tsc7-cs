// ported from: internal/fourslash/tests/semanticModernClassificationVariables_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationVariablesCase
{
    public static void TestSemanticModernClassificationVariables(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("  var x = 9, y1 = [x];\n  try {\n    for (const s of y1) { x = s }\n  } catch (e) {\n    throw y1;\n  }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable.declaration", Text = "x" }, new F.SemanticToken { Type = "variable.declaration", Text = "y1" }, new F.SemanticToken { Type = "variable", Text = "x" }, new F.SemanticToken { Type = "variable.declaration.readonly.local", Text = "s" }, new F.SemanticToken { Type = "variable", Text = "y1" }, new F.SemanticToken { Type = "variable", Text = "x" }, new F.SemanticToken { Type = "variable.readonly.local", Text = "s" }, new F.SemanticToken { Type = "variable.declaration.local", Text = "e" }, new F.SemanticToken { Type = "variable", Text = "y1" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
