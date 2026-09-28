// ported from: internal/fourslash/tests/importNameCodeFixOptionalImport1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixOptionalImport1Case
{
    public static void TestImportNameCodeFixOptionalImport1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a/f1.ts\n[|foo/*0*/();|]\n// @Filename: a/node_modules/bar/index.ts\nexport function foo() {};\n// @Filename: a/foo.ts\nexport { foo } from \"bar\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"bar\";\n\nfoo();", "import { foo } from \"./foo\";\n\nfoo();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
