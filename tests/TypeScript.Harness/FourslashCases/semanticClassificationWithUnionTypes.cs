// ported from: internal/fourslash/tests/semanticClassificationWithUnionTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationWithUnionTypesCase
{
    public static void TestSemanticClassificationWithUnionTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("module /*0*/M {\n    export interface /*1*/I {\n    }\n}\n\ninterface /*2*/I {\n}\nclass /*3*/C {\n}\n\nvar M: /*4*/M./*5*/I | /*6*/I | /*7*/C;\nvar I: typeof M | typeof /*8*/C;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable", Text = "M" }, new F.SemanticToken { Type = "interface.declaration", Text = "I" }, new F.SemanticToken { Type = "interface.declaration", Text = "I" }, new F.SemanticToken { Type = "class.declaration", Text = "C" }, new F.SemanticToken { Type = "variable.declaration", Text = "M" }, new F.SemanticToken { Type = "variable", Text = "M" }, new F.SemanticToken { Type = "interface", Text = "I" }, new F.SemanticToken { Type = "interface", Text = "I" }, new F.SemanticToken { Type = "class", Text = "C" }, new F.SemanticToken { Type = "class.declaration", Text = "I" }, new F.SemanticToken { Type = "variable", Text = "M" }, new F.SemanticToken { Type = "class", Text = "C" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
