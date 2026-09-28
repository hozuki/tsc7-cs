// ported from: internal/fourslash/tests/completionsImportFromJSXTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImportFromJSXTagCase
{
    public static void TestCompletionsImportFromJSXTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @Filename: /types.d.ts\ndeclare namespace JSX {\n  interface IntrinsicElements { a }\n}\n// @Filename: /Box.tsx\nexport function Box(props: any) { return null; }\n// @Filename: /App.tsx\nexport function App() {\n  return (\n    <div className=\"App\">\n      <Box/**/\n    </div>\n  )\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "Box", Source = "./Box", Description = "Add import from \"./Box\"", NewFileContent = "import { Box } from \"./Box\";\n\nexport function App() {\n  return (\n    <div className=\"App\">\n      <Box\n    </div>\n  )\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
