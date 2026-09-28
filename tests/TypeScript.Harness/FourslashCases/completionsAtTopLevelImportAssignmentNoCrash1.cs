// ported from: internal/fourslash/tests/completionsAtTopLevelImportAssignmentNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsAtTopLevelImportAssignmentNoCrash1Case
{
    public static void TestCompletionsAtTopLevelImportAssignmentNoCrash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /a.ts\nimport x =/*1*/\nclass Foo {}\n// @filename: /b.ts\nimport x = /*2*/\nclass Foo {}\n// @filename: /c.ts\nimport x =/*3*/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [] }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done();
            }
        });
    }

}
