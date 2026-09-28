// ported from: internal/fourslash/tests/importNameCodeFix_reExportDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_reExportDefaultCase
{
    public static void TestImportNameCodeFix_reExportDefault(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /user.ts\nfoo;\n// @Filename: /user2.ts\nunnamed;\n// @Filename: /user3.ts\nreExportUnnamed;\n// @Filename: /reExportNamed.ts\nexport { default } from \"./named\";\n// @Filename: /reExportUnnamed.ts\nexport { default } from \"./unnamed\";\n// @Filename: /named.ts\nfunction foo() {}\nexport default foo;\n// @Filename: /unnamed.ts\nexport default 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/user.ts");
                f.VerifyImportFixAtPosition(t, ["import foo from \"./named\";\n\nfoo;", "import foo from \"./reExportNamed\";\n\nfoo;"], null!);
                f.GoToFile(t, "/user2.ts");
                f.VerifyImportFixAtPosition(t, ["import unnamed from \"./unnamed\";\n\nunnamed;", "import unnamed from \"./reExportUnnamed\";\n\nunnamed;"], null!);
                f.GoToFile(t, "/user3.ts");
                f.VerifyImportFixAtPosition(t, ["import reExportUnnamed from \"./reExportUnnamed\";\n\nreExportUnnamed;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
