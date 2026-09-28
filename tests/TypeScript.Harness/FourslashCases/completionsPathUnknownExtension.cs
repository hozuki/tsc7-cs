// ported from: internal/fourslash/tests/completionsPathUnknownExtension_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsPathUnknownExtensionCase
{
    public static void TestCompletionsPathUnknownExtension(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: src/some-file.ruhroh\n/* This is just a test file that needs to exist. */\n\n// @filename: package.json\n{\n    \"imports\": {\n        \"#/*\": \"./src/*\"\n    }\n}\n\n// @filename: src/globals.d.ts\ndeclare module \"*.ruhroh\";\n\n// @filename: src/a.mts\nimport \"#//*$*/\"\n\n// @filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"preserve\",\n        \"moduleResolution\": \"bundler\",\n        \"rootDir\": \"src\"\n    },\n    \"include\": [\"src\"]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "$", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["some-file.ruhroh"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
