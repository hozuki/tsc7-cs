// ported from: internal/fourslash/tests/formattingJsxTexts2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingJsxTexts2Case
{
    public static void TestFormattingJsxTexts2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\nconst a = (\n    <div>\n  foo\n          </div>\n);\n\nconst b = (\n    <div>\n  {     foo  }\n          </div>\n);\n\nconst c = (\n    <div>\n    foo\n  {     foobar  }\n  bar\n          </div>\n);\n\nconst d = \n    <div>\n  foo\n          </div>;\n\nconst e = \n    <div>\n  {     foo  }\n          </div>\n\nconst f = \n    <div>\n    foo\n  {     foobar  }\n  bar\n          </div>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "const a = (\n    <div>\n        foo\n    </div>\n);\n\nconst b = (\n    <div>\n        {foo}\n    </div>\n);\n\nconst c = (\n    <div>\n        foo\n        {foobar}\n        bar\n    </div>\n);\n\nconst d =\n    <div>\n        foo\n    </div>;\n\nconst e =\n    <div>\n        {foo}\n    </div>\n\nconst f =\n    <div>\n        foo\n        {foobar}\n        bar\n    </div>");
            }
            finally
            {
                done();
            }
        });
    }

}
