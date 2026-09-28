// ported from: internal/fourslash/tests/semanticClassificatonTypeAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificatonTypeAliasCase
{
    public static void TestSemanticClassificatonTypeAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type /*0*/Alias = number\nvar x: /*1*/Alias;\nvar y = </*2*/Alias>{};\nfunction f(x: /*3*/Alias): /*4*/Alias { return undefined; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "type.declaration", Text = "Alias" }, new F.SemanticToken { Type = "variable.declaration", Text = "x" }, new F.SemanticToken { Type = "type", Text = "Alias" }, new F.SemanticToken { Type = "variable.declaration", Text = "y" }, new F.SemanticToken { Type = "type", Text = "Alias" }, new F.SemanticToken { Type = "function.declaration", Text = "f" }, new F.SemanticToken { Type = "parameter.declaration", Text = "x" }, new F.SemanticToken { Type = "type", Text = "Alias" }, new F.SemanticToken { Type = "type", Text = "Alias" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
