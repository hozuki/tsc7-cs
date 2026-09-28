// ported from: internal/fourslash/tests/completionsInRequire_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsInRequireCase
{
    public static void TestCompletionsInRequire(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: foo.js\nvar foo = require(\"/**/\"\n\nfoo();\n\n/**\n * @return {void}\n */\nfunction foo() {\n}\n// @Filename: package.json\n { \"dependencies\": { \"fake-module\": \"latest\" } }\n// @Filename: node_modules/fake-module/index.js\n/* fake-module */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["fake-module"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
