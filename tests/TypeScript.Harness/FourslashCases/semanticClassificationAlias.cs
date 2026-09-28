// ported from: internal/fourslash/tests/semanticClassificationAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemanticClassificationAliasCase
{
    public static void TestSemanticClassificationAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport type x = number;\nexport class y {};\n// @Filename: /b.ts\nimport { /*0*/x, /*1*/y } from \"./a\";\nconst v: /*2*/x = /*3*/y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable.declaration.readonly", Text = "v" }, new F.SemanticToken { Type = "type", Text = "x" }, new F.SemanticToken { Type = "class", Text = "y" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
