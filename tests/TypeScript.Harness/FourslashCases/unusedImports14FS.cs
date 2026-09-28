// ported from: internal/fourslash/tests/unusedImports14FS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedImports14FSCase
{
    public static void TestUnusedImports14FS(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n// @Filename: file2.ts\n[| import /* 1 */ A /* 2 */, /* 3 */ { /* 4 */ x /* 5 */ } /* 6 */ from './a'; |]\nconsole.log(A);\n// @Filename: file1.ts\nexport default 10;\nexport var x = 10;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "import /* 1 */ A /* 2 */ /* 6 */ from './a';", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
