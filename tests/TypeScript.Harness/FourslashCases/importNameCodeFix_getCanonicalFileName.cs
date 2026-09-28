// ported from: internal/fourslash/tests/importNameCodeFix_getCanonicalFileName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_getCanonicalFileNameCase
{
    public static void TestImportNameCodeFix_getCanonicalFileName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /howNow/node_modules/brownCow/index.d.ts\nexport const foo: number;\n// @Filename: /howNow/a.ts\nfoo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/howNow/a.ts");
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"brownCow\";\n\nfoo;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
