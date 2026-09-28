// ported from: internal/fourslash/tests/completionForStringLiteralRelativeImport5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralRelativeImport5Case
{
    public static void TestCompletionForStringLiteralRelativeImport5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @rootDirs: /repo/src1,/repo/src2/,/repo/generated1,/repo/generated2/\n// @Filename: /dir/secret_file.ts\n/*secret_file*/\n// @Filename: /repo/src1/dir/test1.ts\nimport * as foo1 from \".//*import_as1*/\nimport foo2 = require(\".//*import_equals1*/\nvar foo3 = require(\".//*require1*/\n// @Filename: /repo/src2/dir/test2.ts\nimport * as foo1 from \"..//*import_as2*/\nimport foo2 = require(\"..//*import_equals2*/\nvar foo3 = require(\"..//*require2*/\n// @Filename: /repo/src2/index.ts\nimport * as foo1 from \".//*import_as3*/\nimport foo2 = require(\".//*import_equals3*/\nvar foo3 = require(\".//*require3*/\n// @Filename: /repo/generated1/dir/f1.ts\n/*f1*/\n// @Filename: /repo/generated2/dir/f2.ts\n/*f2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["import_as1", "import_equals1", "require1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["f1", "f2", "test2"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["import_as2", "import_equals2", "require2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["dir", "index"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["import_as3", "import_equals3", "require3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["dir"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
