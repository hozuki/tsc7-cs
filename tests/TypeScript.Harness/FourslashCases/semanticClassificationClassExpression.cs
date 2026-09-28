// ported from: internal/fourslash/tests/semanticClassificationClassExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationClassExpressionCase
{
    public static void TestSemanticClassificationClassExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = class /*0*/C {}\nclass /*1*/C {}\nclass /*2*/D extends class /*3*/B{} { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "class.declaration", Text = "x" }, new F.SemanticToken { Type = "class.declaration", Text = "C" }, new F.SemanticToken { Type = "class.declaration", Text = "C" }, new F.SemanticToken { Type = "class.declaration", Text = "D" }, new F.SemanticToken { Type = "class.declaration", Text = "B" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
