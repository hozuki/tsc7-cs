// ported from: internal/fourslash/tests/semanticClassificationJSX_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationJSXCase
{
    public static void TestSemanticClassificationJSX(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.tsx\nconst Component = () => <div>Hello</div>;\nconst afterJSX = 42;\nconst alsoAfterJSX = \"test\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.tsx");
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "function.declaration.readonly", Text = "Component" }, new F.SemanticToken { Type = "variable.declaration.readonly", Text = "afterJSX" }, new F.SemanticToken { Type = "variable.declaration.readonly", Text = "alsoAfterJSX" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
