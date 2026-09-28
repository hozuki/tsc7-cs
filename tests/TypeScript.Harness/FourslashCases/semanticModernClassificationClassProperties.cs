// ported from: internal/fourslash/tests/semanticModernClassificationClassProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationClassPropertiesCase
{
    public static void TestSemanticModernClassificationClassProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A { \n  private y: number;\n  constructor(public x : number, _y : number) { this.y = _y; }\n  get z() : number { return this.x + this.y; }\n  set a(v: number) { }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "class.declaration", Text = "A" }, new F.SemanticToken { Type = "property.declaration", Text = "y" }, new F.SemanticToken { Type = "parameter.declaration", Text = "x" }, new F.SemanticToken { Type = "parameter.declaration", Text = "_y" }, new F.SemanticToken { Type = "property", Text = "y" }, new F.SemanticToken { Type = "parameter", Text = "_y" }, new F.SemanticToken { Type = "property.declaration", Text = "z" }, new F.SemanticToken { Type = "property", Text = "x" }, new F.SemanticToken { Type = "property", Text = "y" }, new F.SemanticToken { Type = "property.declaration", Text = "a" }, new F.SemanticToken { Type = "parameter.declaration", Text = "v" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
