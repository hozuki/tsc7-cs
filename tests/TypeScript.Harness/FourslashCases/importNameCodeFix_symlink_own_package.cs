// ported from: internal/fourslash/tests/importNameCodeFix_symlink_own_package_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_symlink_own_packageCase
{
    public static void TestImportNameCodeFix_symlink_own_package(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /packages/b/b0.ts\n// @Symlink: /node_modules/b/b0.ts\nx;\n// @Filename: /packages/b/b1.ts\n// @Symlink: /node_modules/b/b1.ts\nimport { a } from \"a\";\nexport const x = 0;\n// @Filename: /packages/a/index.d.ts\n// @Symlink: /node_modules/a/index.d.ts\nexport const a: number;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/packages/b/b0.ts");
                f.VerifyImportFixAtPosition(t, ["import { x } from \"./b1\";\n\nx;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
