// ported from: internal/fourslash/tests/completionListInImportClause05_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInImportClause05Case
{
    public static void TestCompletionListInImportClause05(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: app.ts\nimport * as A from \"/*1*/\";\n// @Filename: /node_modules/@types/a__b/index.d.ts\ndeclare module \"@e/f\" { function fun(): string; }\n// @Filename: /node_modules/@types/c__d/index.d.ts\nexport declare let x: number;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["@e/f", "@a/b", "@c/d"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
