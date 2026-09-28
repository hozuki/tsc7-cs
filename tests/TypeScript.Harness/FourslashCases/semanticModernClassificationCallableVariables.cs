// ported from: internal/fourslash/tests/semanticModernClassificationCallableVariables_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationCallableVariablesCase
{
    public static void TestSemanticModernClassificationCallableVariables(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A { onEvent: () => void; }\nconst x = new A().onEvent;\nconst match = (s: any) => x();\nconst other = match;\nmatch({ other });\ninterface B = { (): string; }; var b: B\nvar s: String;\nvar t: { (): string; foo: string};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "class.declaration", Text = "A" }, new F.SemanticToken { Type = "method.declaration", Text = "onEvent" }, new F.SemanticToken { Type = "function.declaration.readonly", Text = "x" }, new F.SemanticToken { Type = "class", Text = "A" }, new F.SemanticToken { Type = "method", Text = "onEvent" }, new F.SemanticToken { Type = "function.declaration.readonly", Text = "match" }, new F.SemanticToken { Type = "parameter.declaration", Text = "s" }, new F.SemanticToken { Type = "function.readonly", Text = "x" }, new F.SemanticToken { Type = "function.declaration.readonly", Text = "other" }, new F.SemanticToken { Type = "function.readonly", Text = "match" }, new F.SemanticToken { Type = "function.readonly", Text = "match" }, new F.SemanticToken { Type = "method.declaration", Text = "other" }, new F.SemanticToken { Type = "interface.declaration", Text = "B" }, new F.SemanticToken { Type = "variable.declaration", Text = "b" }, new F.SemanticToken { Type = "interface", Text = "B" }, new F.SemanticToken { Type = "variable.declaration", Text = "s" }, new F.SemanticToken { Type = "interface.defaultLibrary", Text = "String" }, new F.SemanticToken { Type = "variable.declaration", Text = "t" }, new F.SemanticToken { Type = "property.declaration", Text = "foo" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
