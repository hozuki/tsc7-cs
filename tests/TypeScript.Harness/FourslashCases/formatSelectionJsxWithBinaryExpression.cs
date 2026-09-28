// ported from: internal/fourslash/tests/formatSelectionJsxWithBinaryExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSelectionJsxWithBinaryExpressionCase
{
    public static void TestFormatSelectionJsxWithBinaryExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\nfunction TestWidget() {\n    const test = true;\n    return (\n        <div>\n            {test &&\n                <div>\n /*1*/                <div>some text</div>/*2*/\n                    <div>some text</div>\n                    <div>some text</div>\n                </div>\n            }\n            <div>some text</div>\n        </div>\n    );\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatSelection(t, "1", "2");
                f.VerifyCurrentFileContent(t, "function TestWidget() {\n    const test = true;\n    return (\n        <div>\n            {test &&\n                <div>\n                    <div>some text</div>\n                    <div>some text</div>\n                    <div>some text</div>\n                </div>\n            }\n            <div>some text</div>\n        </div>\n    );\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
