// ported from: internal/fourslash/tests/completionsImport_fromAmbientModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_fromAmbientModuleCase
{
    public static void TestCompletionsImport_fromAmbientModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @Filename: /a.ts\ndeclare module \"m\" {\n    export const x: number;\n}\n// @Filename: /b.ts\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "x", Source = "m", Description = "Add import from \"m\"", NewFileContent = "import { x } from \"m\";\n\n" });
            }
            finally
            {
                done();
            }
        });
    }

}
