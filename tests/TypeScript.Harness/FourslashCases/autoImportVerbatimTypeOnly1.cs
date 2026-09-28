// ported from: internal/fourslash/tests/autoImportVerbatimTypeOnly1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportVerbatimTypeOnly1Case
{
    public static void TestAutoImportVerbatimTypeOnly1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @verbatimModuleSyntax: true\n// @Filename: /mod.ts\nexport const value = 0;\nexport class C { constructor(v: any) {} }\nexport interface I {}\n// @Filename: /a.mts\nconst x: /**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "I", Source = "./mod", Description = "Add import from \"./mod.js\"", AutoImportFix = new LSP.AutoImportFix { ModuleSpecifier = "./mod.js" }, NewFileContent = "import type { I } from \"./mod.js\";\n\nconst x: " });
                f.Insert(t, "I = new C");
                f.VerifyApplyCodeActionFromCompletion(t, null!, new F.ApplyCodeActionFromCompletionOptions { Name = "C", Source = "./mod", Description = "Update import from \"./mod.js\"", AutoImportFix = new LSP.AutoImportFix { ModuleSpecifier = "./mod.js" }, NewFileContent = "import { C, type I } from \"./mod.js\";\n\nconst x: I = new C" });
            }
            finally
            {
                done();
            }
        });
    }

}
