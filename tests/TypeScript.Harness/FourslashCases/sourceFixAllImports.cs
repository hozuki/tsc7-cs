// ported from: internal/fourslash/tests/sourceFixAllImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SourceFixAllImportsCase
{
    public static void TestSourceFixAllImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport const a: number = 1;\n// @Filename: /b.ts\nexport const b: number = 2;\n// @Filename: /main.ts\na;\nb;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/main.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { a } from \"./a\";\nimport { b } from \"./b\";\n\na;\nb;" });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestSourceFixAllCodeAction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport const a: number = 1;\n// @Filename: /b.ts\nexport const b: number = 2;\n// @Filename: /main.ts\na;\nb;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/main.ts");
                f.VerifySourceFixAll(t, "import { a } from \"./a\";\nimport { b } from \"./b\";\n\na;\nb;");
            }
            finally
            {
                done();
            }
        });
    }

}
