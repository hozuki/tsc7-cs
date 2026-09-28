// ported from: internal/fourslash/tests/syntacticClassificationsJsx2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntacticClassificationsJsx2Case
{
    public static void TestSyntacticClassificationsJsx2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file1.tsx\nlet x  = <div.name b = \"some-value\" c = {1}>\n    some jsx text\n</div.name>;\n\nlet y = <element.name attr=\"123\"/>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySemanticTokens(t, [new F.SemanticToken { Type = "variable.declaration", Text = "x" }, new F.SemanticToken { Type = "variable.declaration", Text = "y" }]);
            }
            finally
            {
                done();
            }
        });
    }

}
