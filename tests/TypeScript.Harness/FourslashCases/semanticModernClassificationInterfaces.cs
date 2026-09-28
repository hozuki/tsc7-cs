// ported from: internal/fourslash/tests/semanticModernClassificationInterfaces_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationInterfacesCase
{
    public static void TestSemanticModernClassificationInterfaces(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Pos { x: number, y: number };\nconst p = { x: 1, y: 2 } as Pos;\nconst foo = (o: Pos) => o.x + o.y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "interface.declaration", Text = "Pos" }, new F.SemanticToken { Type = "property.declaration", Text = "x" }, new F.SemanticToken { Type = "property.declaration", Text = "y" }, new F.SemanticToken { Type = "variable.declaration.readonly", Text = "p" }, new F.SemanticToken { Type = "property.declaration", Text = "x" }, new F.SemanticToken { Type = "property.declaration", Text = "y" }, new F.SemanticToken { Type = "interface", Text = "Pos" }, new F.SemanticToken { Type = "function.declaration.readonly", Text = "foo" }, new F.SemanticToken { Type = "parameter.declaration", Text = "o" }, new F.SemanticToken { Type = "interface", Text = "Pos" }, new F.SemanticToken { Type = "parameter", Text = "o" }, new F.SemanticToken { Type = "property", Text = "x" }, new F.SemanticToken { Type = "parameter", Text = "o" }, new F.SemanticToken { Type = "property", Text = "y" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
