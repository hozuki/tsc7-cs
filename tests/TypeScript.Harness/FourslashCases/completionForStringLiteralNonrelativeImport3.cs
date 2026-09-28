// ported from: internal/fourslash/tests/completionForStringLiteralNonrelativeImport3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralNonrelativeImport3Case
{
    public static void TestCompletionForStringLiteralNonrelativeImport3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: tests/test0.ts\nimport * as foo1 from \"fake-module//*import_as0*/\nimport foo2 = require(\"fake-module//*import_equals0*/\nvar foo3 = require(\"fake-module//*require0*/\n// @Filename: package.json\n{ \"dependencies\": { \"fake-module\": \"latest\" } }\n// @Filename: node_modules/fake-module/ts.ts\n/*ts*/\n// @Filename: node_modules/fake-module/tsx.tsx\n/*tsx*/\n// @Filename: node_modules/fake-module/dts.d.ts\n/*dts*/\n// @Filename: node_modules/fake-module/js.js\n/*js*/\n// @Filename: node_modules/fake-module/jsx.jsx\n/*jsx*/\n// @Filename: node_modules/fake-module/repeated.js\n/*repeatedjs*/\n// @Filename: node_modules/fake-module/repeated.jsx\n/*repeatedjsx*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["import_as0", "import_equals0", "require0"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["dts", "js", "jsx", "repeated", "ts", "tsx"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
