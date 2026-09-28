// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsOnlyFromClosestScope1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsOnlyFromClosestScope1Case
{
    public static void TestPathCompletionsPackageJsonImportsOnlyFromClosestScope1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /package.json\n{\n  \"imports\": {\n    \"#thing\": \"./src/something.ts\"\n  }\n}\n// @Filename: /src/package.json\n{}\n// @Filename: /src/something.ts\nexport function something(name: string): any;\n// @Filename: /src/a.ts\nimport {} from \"/*1*/\";\n// @Filename: /a.ts\nimport {} from \"/*2*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["#thing"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
