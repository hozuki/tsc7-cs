// ported from: internal/fourslash/tests/importNameCodeFixNewImportAllowSyntheticDefaultImports0_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportAllowSyntheticDefaultImports0Case
{
    public static void TestImportNameCodeFixNewImportAllowSyntheticDefaultImports0(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @AllowSyntheticDefaultImports: true\n// @Filename: a/f1.ts\n[|export var x = 0;\nbar/*0*/();|]\n// @Filename: a/foo.d.ts\ndeclare function bar(): number;\nexport = bar;\nexport as namespace bar;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import bar from \"./foo\";\n\nexport var x = 0;\nbar();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
