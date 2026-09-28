// ported from: internal/fourslash/tests/completionForStringLiteralNonrelativeImport4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralNonrelativeImport4Case
{
    public static void TestCompletionForStringLiteralNonrelativeImport4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: dir1/dir2/dir3/dir4/test0.ts\nimport * as foo1 from \"f/*import_as0*/\nimport foo4 = require(\"f/*import_equals0*/\nvar foo7 = require(\"f/*require0*/\n// @Filename: package.json\n{ \"dependencies\": { \"fake-module\": \"latest\" } }\n// @Filename: node_modules/fake-module/ts.ts\n\n// @Filename: dir1/package.json\n{ \"dependencies\": { \"fake-module2\": \"latest\" } }\n// @Filename: dir1/node_modules/fake-module2/index.ts\n\n// @Filename: dir1/dir2/dir3/package.json\n{ \"dependencies\": { \"fake-module3\": \"latest\" } }\n// @Filename: dir1/dir2/dir3/node_modules/fake-module3/ts.ts\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["fake-module3", "fake-module2", "fake-module"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
