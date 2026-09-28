// ported from: internal/fourslash/tests/completionForStringLiteralNonrelativeImport7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralNonrelativeImport7Case
{
    public static void TestCompletionForStringLiteralNonrelativeImport7(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @baseUrl: tests/cases/fourslash/modules\n// @Filename: tests/test0.ts\nimport * as foo1 from \"mod/*import_as0*/\nimport foo2 = require(\"mod/*import_equals0*/\nvar foo3 = require(\"mod/*require0*/\n// @Filename: modules/module.ts\nexport var x = 5;\n// @Filename: package.json\n{ \"dependencies\": { \"module-from-node\": \"latest\" } }\n// @Filename: node_modules/module-from-node/index.ts\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["module", "module-from-node"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
