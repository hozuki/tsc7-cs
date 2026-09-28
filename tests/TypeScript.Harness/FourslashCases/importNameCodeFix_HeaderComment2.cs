// ported from: internal/fourslash/tests/importNameCodeFix_HeaderComment2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_HeaderComment2Case
{
    public static void TestImportNameCodeFix_HeaderComment2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport const foo = 0;\n// @Filename: /b.ts\nexport const bar = 0;\n// @Filename: /c.ts\n/*--------------------\n *  Copyright Header\n *--------------------*/\n\nconst afterHeader = 1;\n\n// non-header comment\nimport { bar } from \"./b\";\nfoo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/c.ts");
                f.VerifyImportFixAtPosition(t, ["/*--------------------\n *  Copyright Header\n *--------------------*/\n\nconst afterHeader = 1;\n\nimport { foo } from \"./a\";\n// non-header comment\nimport { bar } from \"./b\";\nfoo;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
