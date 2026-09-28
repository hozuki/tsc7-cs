// ported from: internal/fourslash/tests/semanticClassificationInTemplateExpressions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationInTemplateExpressionsCase
{
    public static void TestSemanticClassificationInTemplateExpressions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("module /*0*/M {\n    export class /*1*/C {\n        static x;\n    }\n    export enum /*2*/E {\n        E1 = 0\n    }\n}\n`abcd${ /*3*/M./*4*/C.x + /*5*/M./*6*/E.E1}efg`"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "namespace.declaration", Text = "M" }, new F.SemanticToken { Type = "class.declaration", Text = "C" }, new F.SemanticToken { Type = "property.declaration.static", Text = "x" }, new F.SemanticToken { Type = "enum.declaration", Text = "E" }, new F.SemanticToken { Type = "enumMember.declaration.readonly", Text = "E1" }, new F.SemanticToken { Type = "namespace", Text = "M" }, new F.SemanticToken { Type = "class", Text = "C" }, new F.SemanticToken { Type = "property.static", Text = "x" }, new F.SemanticToken { Type = "namespace", Text = "M" }, new F.SemanticToken { Type = "enum", Text = "E" }, new F.SemanticToken { Type = "enumMember.readonly", Text = "E1" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
