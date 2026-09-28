// ported from: internal/fourslash/tests/syntacticClassifications1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntacticClassifications1Case
{
    public static void TestSyntacticClassifications1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// comment\nnamespace M {\n    var v = 0 + 1;\n    var s = \"string\";\n\n    class C<T> {\n    }\n\n    enum E {\n    }\n\n    interface I {\n    }\n\n    namespace M1.M2 {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "namespace.declaration", Text = "M" }, new F.SemanticToken { Type = "variable.declaration.local", Text = "v" }, new F.SemanticToken { Type = "variable.declaration.local", Text = "s" }, new F.SemanticToken { Type = "class.declaration", Text = "C" }, new F.SemanticToken { Type = "typeParameter.declaration", Text = "T" }, new F.SemanticToken { Type = "enum.declaration", Text = "E" }, new F.SemanticToken { Type = "interface.declaration", Text = "I" }, new F.SemanticToken { Type = "namespace.declaration", Text = "M1" }, new F.SemanticToken { Type = "namespace.declaration", Text = "M2" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
