// ported from: internal/fourslash/tests/unusedImports11FS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedImports11FSCase
{
    public static void TestUnusedImports11FS(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n// @Filename: file2.ts\n[| import f1, * as s from \"./file1\"; |]\ns.f2('hello');\n// @Filename: file1.ts\nexport var v1;\nexport function f1(n: number){}\nexport function f2(s: string){};\nexport default f1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "import * as s from \"./file1\";", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
