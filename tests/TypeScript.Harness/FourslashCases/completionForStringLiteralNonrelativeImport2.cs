// ported from: internal/fourslash/tests/completionForStringLiteralNonrelativeImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralNonrelativeImport2Case
{
    public static void TestCompletionForStringLiteralNonrelativeImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tests/test0.ts\nimport * as foo1 from \"fake-module//*import_as0*/\nimport foo2 = require(\"fake-module//*import_equals0*/\nvar foo3 = require(\"fake-module//*require0*/\n// @Filename: package.json\n{ \"dependencies\": { \"fake-module\": \"latest\" }, \"devDependencies\": { \"fake-module-dev\": \"latest\" } }\n// @Filename: node_modules/fake-module/repeated.ts\n/*repeatedts*/\n// @Filename: node_modules/fake-module/repeated.tsx\n/*repeatedtsx*/\n// @Filename: node_modules/fake-module/repeated.d.ts\n/*repeateddts*/\n// @Filename: node_modules/fake-module/other.js\n/*other*/\n// @Filename: node_modules/fake-module/other2.js\n/*other2*/\n// @Filename: node_modules/unlisted-module/index.js\n/*unlisted-module*/\n// @Filename: ambient.ts\ndeclare module \"fake-module/other\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["import_as0", "import_equals0", "require0"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["other", "repeated"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
