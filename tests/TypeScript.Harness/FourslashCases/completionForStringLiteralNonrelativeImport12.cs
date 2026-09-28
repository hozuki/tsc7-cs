// ported from: internal/fourslash/tests/completionForStringLiteralNonrelativeImport12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralNonrelativeImport12Case
{
    public static void TestCompletionForStringLiteralNonrelativeImport12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tests/test0.ts\nimport * as foo1 from \"m/*import_as0*/\nimport foo2 = require(\"m/*import_equals0*/\nvar foo3 = require(\"m/*require0*/\n// @Filename: package.json\n{\n    \"dependencies\": { \"module\": \"latest\" },\n    \"devDependencies\": { \"dev-module\": \"latest\" },\n    \"optionalDependencies\": { \"optional-module\": \"latest\" },\n    \"peerDependencies\": { \"peer-module\": \"latest\" }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["module", "dev-module", "peer-module", "optional-module"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
