// ported from: internal/fourslash/tests/semanticModernClassificationConstructorTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticModernClassificationConstructorTypesCase
{
    public static void TestSemanticModernClassificationConstructorTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nObject.create(null);\nconst x = Promise.resolve(Number.MAX_VALUE);\nif (x instanceof Promise) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "class.defaultLibrary", Text = "Object" }, new F.SemanticToken { Type = "method.defaultLibrary", Text = "create" }, new F.SemanticToken { Type = "variable.declaration.readonly", Text = "x" }, new F.SemanticToken { Type = "class.defaultLibrary", Text = "Number" }, new F.SemanticToken { Type = "property.readonly.defaultLibrary", Text = "MAX_VALUE" }, new F.SemanticToken { Type = "variable.readonly", Text = "x" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
