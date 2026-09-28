// ported from: internal/fourslash/tests/importNameCodeFix_avoidRelativeNodeModules_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_avoidRelativeNodeModulesCase
{
    public static void TestImportNameCodeFix_avoidRelativeNodeModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a/index.d.ts\n// @Symlink: /b/node_modules/a/index.d.ts\n// @Symlink: /c/node_modules/a/index.d.ts\nexport const a: number;\n// @Filename: /b/index.ts\n// @Symlink: /c/node_modules/b/index.d.ts\nimport { a } from 'a'\nexport const b: number;\n// @Filename: /c/a_user.ts\nimport { a } from \"a\";\n// @Filename: /c/foo.ts\n[|import { b } from \"b\";\na;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/c/foo.ts");
                f.VerifyImportFixAtPosition(t, ["import { a } from \"a\";\nimport { b } from \"b\";\na;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
