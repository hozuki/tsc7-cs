// ported from: internal/fourslash/tests/completionsImport_typeOnly_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_typeOnlyCase
{
    public static void TestCompletionsImport_typeOnly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: esnext\n// @moduleResolution: bundler\n// @Filename: /a.ts\nexport class A {}\nexport class B {}\n// @Filename: /b.ts\nimport type { A } from './a';\nconst b: B/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "B", Source = "./a", Description = "Update import from \"./a\"", UserPreferences = null!, NewFileContent = "import type { A, B } from './a';\nconst b: B" });
            }
            finally
            {
                done();
            }
        });
    }

}
