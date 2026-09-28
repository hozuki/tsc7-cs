// ported from: internal/fourslash/tests/importNameCodeFixOptionalImport0_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixOptionalImport0Case
{
    public static void TestImportNameCodeFixOptionalImport0(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a/f1.ts\n[|import * as ns from \"./foo\";\nfoo/*0*/();|]\n// @Filename: a/foo/bar.ts\nexport function foo() {};\n// @Filename: a/foo.ts\nexport { foo } from \"./foo/bar\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import * as ns from \"./foo\";\nns.foo();", "import * as ns from \"./foo\";\nimport { foo } from \"./foo\";\nfoo();", "import * as ns from \"./foo\";\nimport { foo } from \"./foo/bar\";\nfoo();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
