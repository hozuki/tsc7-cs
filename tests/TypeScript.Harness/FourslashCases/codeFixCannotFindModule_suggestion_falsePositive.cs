// ported from: internal/fourslash/tests/codeFixCannotFindModule_suggestion_falsePositive_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixCannotFindModule_suggestion_falsePositiveCase
{
    public static void TestCodeFixCannotFindModule_suggestion_falsePositive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @module: commonjs\n// @resolveJsonModule: true\n// @strict: true\n// @Filename: /node_modules/foo/bar.json\n{ \"a\": 0 }\n// @Filename: /a.ts\nimport abs = require([|\"foo/bar.json\"|]);\nabs;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToFile(t, "/a.ts");
                f.VerifySuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
