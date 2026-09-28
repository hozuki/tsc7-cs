// ported from: internal/fourslash/tests/quickInfoImportNonunicodePath_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoImportNonunicodePathCase
{
    public static void TestQuickInfoImportNonunicodePath(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /\u6C5F\u5357\u4ECA\u4F55\u5728/tmp.ts\nexport const foo = 1;\n// @Filename: /test.ts\nimport { foo } from \"./\u6C5F\u5357/*1*/\u4ECA\u4F55\u5728/tmp\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "module \"./\u6c5f\u5357\u4eca\u4f55\u5728/tmp\"", "");
            }
            finally
            {
                done();
            }
        });
    }

}
