// ported from: internal/fourslash/tests/semanticModernClassificationPrivateIdentifiers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationPrivateIdentifiersCase
{
    public static void TestSemanticModernClassificationPrivateIdentifiers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n\t#privateField = 1;\n\t#privateMethod() {}\n\n\ttest() {\n\t\tthis.#privateField;\n\t\tthis.#privateMethod();\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "class.declaration", Text = "Foo" }, new F.SemanticToken { Type = "property.declaration", Text = "#privateField" }, new F.SemanticToken { Type = "method.declaration", Text = "#privateMethod" }, new F.SemanticToken { Type = "method.declaration", Text = "test" }, new F.SemanticToken { Type = "property", Text = "#privateField" }, new F.SemanticToken { Type = "method", Text = "#privateMethod" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
