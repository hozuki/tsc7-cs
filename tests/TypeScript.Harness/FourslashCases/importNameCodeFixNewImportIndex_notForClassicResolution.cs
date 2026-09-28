// ported from: internal/fourslash/tests/importNameCodeFixNewImportIndex_notForClassicResolution_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportIndex_notForClassicResolutionCase
{
    public static void TestImportNameCodeFixNewImportIndex_notForClassicResolution(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: classic\n// @Filename: /a/index.ts\nexport const foo = 0;\n// @Filename: /node_modules/x/index.d.ts\nexport const bar = 0;\n// @Filename: /b.ts\n[|foo;|]\n// @Filename: /c.ts\n[|bar;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a/index.ts");
                f.GoToFile(t, "/b.ts");
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"./a/index\";\n\nfoo;"], null!);
                f.GoToFile(t, "/c.ts");
                f.VerifyImportFixAtPosition(t, ["import { bar } from \"./node_modules/x/index\";\n\nbar;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
