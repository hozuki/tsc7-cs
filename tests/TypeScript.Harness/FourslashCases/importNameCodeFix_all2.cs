// ported from: internal/fourslash/tests/importNameCodeFix_all2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_all2Case
{
    public static void TestImportNameCodeFix_all2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /path.ts\nexport declare function join(): void;\n// @Filename: /os.ts\nexport declare function homedir(): void;\n// @Filename: /index.ts\n\njoin();\nhomedir();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/index.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { homedir } from \"./os\";\nimport { join } from \"./path\";\n\njoin();\nhomedir();" });
            }
            finally
            {
                done();
            }
        });
    }

}
