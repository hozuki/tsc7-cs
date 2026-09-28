// ported from: internal/fourslash/tests/importCompletionsPackageJsonImportsConditions1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportCompletionsPackageJsonImportsConditions1Case
{
    public static void TestImportCompletionsPackageJsonImportsConditions1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /package.json\n{\n  \"imports\": {\n    \"#thing\": {\n        \"types\": { \"import\": \"./types-esm/thing.d.mts\", \"require\": \"./types/thing.d.ts\" },\n        \"default\": { \"import\": \"./esm/thing.mjs\", \"require\": \"./dist/thing.js\" }\n     }\n  }\n}\n// @Filename: /types/thing.d.ts\nexport function something(name: string): any;\n// @Filename: /src/foo.ts\nimport {} from \"/*1*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["#thing"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
