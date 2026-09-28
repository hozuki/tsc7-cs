// ported from: internal/fourslash/tests/semanticModernClassificationFunctions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationFunctionsCase
{
    public static void TestSemanticModernClassificationFunctions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(p1) {\n  return foo(Math.abs(p1))\n}\n`/${window.location}`.split(\"/\").forEach(s => foo(s));"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "function.declaration", Text = "foo" }, new F.SemanticToken { Type = "parameter.declaration", Text = "p1" }, new F.SemanticToken { Type = "function", Text = "foo" }, new F.SemanticToken { Type = "variable.defaultLibrary", Text = "Math" }, new F.SemanticToken { Type = "method.defaultLibrary", Text = "abs" }, new F.SemanticToken { Type = "parameter", Text = "p1" }, new F.SemanticToken { Type = "variable.defaultLibrary", Text = "window" }, new F.SemanticToken { Type = "property.defaultLibrary", Text = "location" }, new F.SemanticToken { Type = "method.defaultLibrary", Text = "split" }, new F.SemanticToken { Type = "method.defaultLibrary", Text = "forEach" }, new F.SemanticToken { Type = "parameter.declaration", Text = "s" }, new F.SemanticToken { Type = "function", Text = "foo" }, new F.SemanticToken { Type = "parameter", Text = "s" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
