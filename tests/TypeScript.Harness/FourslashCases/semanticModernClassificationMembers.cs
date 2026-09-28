// ported from: internal/fourslash/tests/semanticModernClassificationMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationMembersCase
{
    public static void TestSemanticModernClassificationMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n  static x = 9;\n  f = 9;\n  async m() { return A.x + await this.m(); };\n  get s() { return this.f; \n  static t() { return new A().f; };\n  constructor() {}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "class.declaration", Text = "A" }, new F.SemanticToken { Type = "property.declaration.static", Text = "x" }, new F.SemanticToken { Type = "property.declaration", Text = "f" }, new F.SemanticToken { Type = "method.declaration.async", Text = "m" }, new F.SemanticToken { Type = "class", Text = "A" }, new F.SemanticToken { Type = "property.static", Text = "x" }, new F.SemanticToken { Type = "method.async", Text = "m" }, new F.SemanticToken { Type = "property.declaration", Text = "s" }, new F.SemanticToken { Type = "property", Text = "f" }, new F.SemanticToken { Type = "method.declaration.static", Text = "t" }, new F.SemanticToken { Type = "class", Text = "A" }, new F.SemanticToken { Type = "property", Text = "f" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
