// ported from: internal/fourslash/tests/importNameCodeFix_types_classic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_types_classicCase
{
    public static void TestImportNameCodeFix_types_classic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: classic\n// @Filename: /node_modules/@types/foo/index.d.ts\nexport const xyz: number;\n// @Filename: /node_modules/bar/index.d.ts\nexport const qrs: number;\n// @Filename: /a.ts\nxyz;\nqrs;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { xyz } from \"foo\";\nimport { qrs } from \"./node_modules/bar/index\";\n\nxyz;\nqrs;" });
            }
            finally
            {
                done();
            }
        });
    }

}
