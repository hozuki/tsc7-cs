// ported from: internal/fourslash/tests/semanticClassificationInstantiatedModuleWithVariableOfSameName2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationInstantiatedModuleWithVariableOfSameName2Case
{
    public static void TestSemanticClassificationInstantiatedModuleWithVariableOfSameName2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("module /*0*/M {\n    export interface /*1*/I {\n    }\n}\n\nmodule /*2*/M {\n    var x = 10;\n}\n\nvar /*3*/M = {\n    foo: 10,\n    bar: 20\n}\n\nvar v: /*4*/M./*5*/I;\n\nvar x = /*6*/M;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "namespace.declaration", Text = "M" }, new F.SemanticToken { Type = "interface.declaration", Text = "I" }, new F.SemanticToken { Type = "namespace.declaration", Text = "M" }, new F.SemanticToken { Type = "variable.declaration.local", Text = "x" }, new F.SemanticToken { Type = "variable.declaration", Text = "M" }, new F.SemanticToken { Type = "property.declaration", Text = "foo" }, new F.SemanticToken { Type = "property.declaration", Text = "bar" }, new F.SemanticToken { Type = "variable.declaration", Text = "v" }, new F.SemanticToken { Type = "namespace", Text = "M" }, new F.SemanticToken { Type = "interface", Text = "I" }, new F.SemanticToken { Type = "variable.declaration", Text = "x" }, new F.SemanticToken { Type = "namespace", Text = "M" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
