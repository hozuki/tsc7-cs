// ported from: internal/fourslash/tests/importCompletionsPackageJsonImportsPattern_capsInPath2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportCompletionsPackageJsonImportsPattern_capsInPath2Case
{
    public static void TestImportCompletionsPackageJsonImportsPattern_capsInPath2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /Dev/package.json\n{\n  \"imports\": {\n    \"#thing/*\": \"./src/*.js\"\n  }\n}\n// @Filename: /Dev/src/something.ts\nexport function something(name: string): any;\n// @Filename: /Dev/a.ts\nimport {} from \"#thing//*2*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["something"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
