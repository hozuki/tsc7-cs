// ported from: internal/fourslash/tests/semanticClassificationModules_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationModulesCase
{
    public static void TestSemanticClassificationModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("module /*0*/M {\n    export var v;\n    export interface /*1*/I {\n    }\n}\n\nvar x: /*2*/M./*3*/I = /*4*/M.v;\nvar y = /*5*/M;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "namespace.declaration", Text = "M" }, new F.SemanticToken { Type = "variable.declaration.local", Text = "v" }, new F.SemanticToken { Type = "interface.declaration", Text = "I" }, new F.SemanticToken { Type = "variable.declaration", Text = "x" }, new F.SemanticToken { Type = "namespace", Text = "M" }, new F.SemanticToken { Type = "interface", Text = "I" }, new F.SemanticToken { Type = "namespace", Text = "M" }, new F.SemanticToken { Type = "variable.local", Text = "v" }, new F.SemanticToken { Type = "variable.declaration", Text = "y" }, new F.SemanticToken { Type = "namespace", Text = "M" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
